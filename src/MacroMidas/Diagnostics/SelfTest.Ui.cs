using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using MacroMidas.Capture;
using MacroMidas.Controls;
using MacroMidas.Engine;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Services;
using MacroMidas.ViewModels;
using MacroMidas.Views;
using MacroMidas.Views.Overlay;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Diagnostics;

// Cliques "de verdade" (mensagens do Windows) na barra de controle, no menu e nos atalhos.
public static partial class SelfTest
{
    private static async Task TestBarClicks(Action<bool, string, string> check)
    {
        PulseOverlayWindow? pulse = null;
        ControlBarWindow? bar = null;
        try
        {
            var paused = 0;
            var stopped = 0;
            var vm = new ControlBarViewModel
            {
                Title = "Autoteste",
                Status = "Cliques na barra",
                PauseCommand = new RelayCommand(() => paused++),
                StopCommand = new RelayCommand(() => stopped++),
                StopRecordingCommand = new RelayCommand(() => { }),
                CancelRecordingCommand = new RelayCommand(() => { }),
                RestOpacity = 0.5
            };

            pulse = new PulseOverlayWindow();
            var screen = pulse.Screens.Primary ?? pulse.Screens.All[0];
            pulse.Configure(Color.Parse("#FFC83D"), 0.35, 2, 18);
            pulse.PlaceOn(screen.Bounds);
            bar = new ControlBarWindow { DataContext = vm };
            bar.Show();
            pulse.Show();
            pulse.StartPulse(1.6);
            await UiProbe.Pump(600);

            var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            var pause = buttons.First(b => ReferenceEquals(b.Command, vm.PauseCommand));
            var stop = buttons.First(b => ReferenceEquals(b.Command, vm.StopCommand));

            var center = stop.PointToScreen(new Point(stop.Bounds.Width / 2, stop.Bounds.Height / 2));
            var under = GetAncestor(WindowFromPoint(new POINT(center.X, center.Y)), GA_ROOT);
            check(under == bar.Hwnd, "Barra: o mouse chega nela mesmo com a borda por cima",
                $"janela sob o botão=0x{under:X} barra=0x{bar.Hwnd:X}");

            var r1 = await UiProbe.ClickAsync(pause);
            check(r1.HitsClient && r1.NotEaten && paused == 1, "Barra: botão Pausar funciona com clique", $"{r1} · cliques={paused}");
            var r2 = await UiProbe.ClickAsync(stop);
            check(r2.HitsClient && r2.NotEaten && stopped == 1, "Barra: botão Parar funciona com clique", $"{r2} · cliques={stopped}");

            var before = pulse.FindDescendantOfType<Grid>()?.Opacity ?? -1;
            await UiProbe.Pump(500);
            var after = pulse.GetVisualDescendants().OfType<Grid>().Skip(1).FirstOrDefault()?.Opacity ?? -1;
            check(Math.Abs(after - 1) > 0.02, "Borda: pulsa (animação rodando)", $"opacidade do brilho={after:0.00}");
        }
        catch (Exception ex)
        {
            check(false, "Barra: cliques", ex.Message);
        }
        finally
        {
            bar?.Close();
            pulse?.Close();
        }
    }

    // Rolagem no modo Lock (janela escolhida e tela inteira) numa caixa de texto com muitas linhas.
    private static async Task TestLockScroll(Action<bool, string, string> check)
    {
        var top = IntPtr.Zero;
        try
        {
            top = CreateWindowExW(0, "STATIC", "Autoteste · rolagem", (uint)WS_OVERLAPPEDWINDOW,
                120, 120, 440, 300, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var edit = CreateWindowExW(0, "EDIT", "", (uint)(WS_CHILD | WS_VISIBLE | WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL),
                0, 0, 400, 240, top, new IntPtr(1), IntPtr.Zero, IntPtr.Zero);
            if (top == IntPtr.Zero || edit == IntPtr.Zero)
            {
                check(false, "Rolagem: janela de teste criada", "");
                return;
            }
            var text = string.Join("\r\n", Enumerable.Range(1, 300).Select(i => $"linha {i}"));
            SetWindowTextW(edit, text);
            ShowWindow(top, SW_SHOWNOACTIVATE);
            await Task.Delay(300);
            SendMessageW(edit, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);
            SendMessageW(edit, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);

            async Task<(int Before, int After)> Run(IInputBackend input, int x, int y)
            {
                var before = (int)SendMessageW(edit, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                var flow = new Flow { Name = "autoteste-rolagem" };
                flow.Steps.Add(new ScrollStep { UsePosition = true, X = x, Y = y, ClickFirst = true, Amount = 4, IntervalMs = 10 });
                var runner = new FlowRunner();
                var done = new TaskCompletionSource<RunFinishedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                runner.Finished += i => done.TrySetResult(i);
                runner.Start(flow, new RunEnvironment { Input = input, Pixels = new ScreenPixelReader(IntPtr.Zero) },
                    new RunOptions { HighPrecisionTimer = false, SkipStartDelay = true });
                await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Task.Delay(300);
                return (before, (int)SendMessageW(edit, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero));
            }

            var w1 = await Run(new WindowMessageBackend(top), 60, 60);
            check(w1.After > w1.Before, "Rolagem (Lock numa janela): desceu a lista sem mexer no seu mouse",
                $"primeira linha visível {w1.Before}→{w1.After}");

            SendMessageW(edit, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);
            SendMessageW(edit, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
            var pt = new POINT(60, 60);
            ClientToScreen(top, ref pt);
            var w2 = await Run(new ScreenMessageBackend(h => h != top), pt.X, pt.Y);
            check(w2.After > w2.Before, "Rolagem (Lock na tela inteira): desceu a lista no ponto escolhido",
                $"primeira linha visível {w2.Before}→{w2.After}");
        }
        catch (Exception ex)
        {
            check(false, "Rolagem", ex.Message);
        }
        finally
        {
            if (top != IntPtr.Zero) DestroyWindow(top);
        }
    }

    // Caminho gravado refeito no modo Lock: a janela recebe os mesmos pontos, na ordem.
    private static async Task TestPathReplay(Action<bool, string, string> check)
    {
        Window? win = null;
        try
        {
            var seen = new List<PixelPoint>();
            var surface = new Border { Background = Brushes.Black };
            win = new Window
            {
                Width = 360, Height = 240, ShowActivated = false, SystemDecorations = SystemDecorations.None,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(160, 160), Content = surface,
                ShowInTaskbar = false,
            };
            surface.PointerMoved += (_, e) =>
            {
                var p = e.GetPosition(win);
                seen.Add(new PixelPoint((int)Math.Round(p.X * win.RenderScaling), (int)Math.Round(p.Y * win.RenderScaling)));
            };
            win.Show();
            await UiProbe.Pump(400);
            var hwnd = UiProbe.HwndOf(win);

            var path = new List<int>();
            for (var i = 0; i < 40; i++)
            {
                path.Add(i == 0 ? 0 : 4);
                path.Add(20 + i * 7);
                path.Add(30 + (int)(Math.Sin(i / 5.0) * 60 + 60));
            }
            var flow = new Flow { Name = "autoteste-caminho" };
            flow.Steps.Add(new MousePathStep { Path = path });
            var runner = new FlowRunner();
            var done = new TaskCompletionSource<RunFinishedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            runner.Finished += i => done.TrySetResult(i);
            runner.Start(flow, new RunEnvironment { Input = new WindowMessageBackend(hwnd), Pixels = new WindowPixelReader(hwnd) },
                new RunOptions { HighPrecisionTimer = true, SkipStartDelay = true });
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await UiProbe.Pump(400);

            var expected = Enumerable.Range(0, 40).Select(i => new PixelPoint(path[i * 3 + 1], path[i * 3 + 2])).ToList();
            var matched = expected.Count(p => seen.Contains(p));
            var last = seen.Count > 0 ? seen[^1] : default;
            check(matched >= 38 && last == expected[^1], "Caminho gravado: o mouse do fluxo passa pelos mesmos pontos, na ordem",
                $"pontos que chegaram {matched}/40 · último ({last.X},{last.Y})");
        }
        catch (Exception ex)
        {
            check(false, "Caminho gravado", ex.Message);
        }
        finally
        {
            win?.Close();
        }
    }

    private static async Task TestMainWindowClicks(Action<bool, string, string> check)
    {
        var originalRoot = AppPaths.Root;
        MainWindow? w = null;
        MainWindowViewModel? vm = null;
        try
        {
            AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "MacroMidasSelfTest", Guid.NewGuid().ToString("N")));
            AppPaths.Ensure();
            var settings = new SettingsService();
            settings.Load();
            w = new MainWindow { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(60, 60) };
            vm = new MainWindowViewModel(settings, new FlowLibrary(), w, systemIntegration: false);
            w.DataContext = vm;
            w.Show();
            await UiProbe.Pump(900);

            var menu = w.GetVisualDescendants().OfType<Menu>().First();
            var file = menu.Items.OfType<MenuItem>().First();
            var r = await UiProbe.ClickAsync(file, 300);
            check(r.HitsClient && file.IsSubMenuOpen, "Menu: “Arquivo” abre com clique", $"{r} · aberto={file.IsSubMenuOpen}");

            var newFlow = file.Items.OfType<MenuItem>().FirstOrDefault(m => (m.Header as string) == "Novo fluxo");
            if (newFlow != null && file.IsSubMenuOpen)
            {
                var count = vm.Flows.Count;
                var r2 = await UiProbe.ClickAsync(newFlow, 300);
                check(vm.Flows.Count == count + 1, "Menu: Arquivo → Novo fluxo funciona", $"{r2} · fluxos {count}→{vm.Flows.Count}");
            }
            file.IsSubMenuOpen = false;
            await UiProbe.Pump(200);

            var runMenu = menu.Items.OfType<MenuItem>().First(m => (m.Header as string) == "Executar");
            MenuItem StartItem() => runMenu.Items.OfType<MenuItem>().First(m => ReferenceEquals(m.Command, vm.StartSelectedCommand));
            await UiProbe.ClickAsync(runMenu, 300);
            var emptyDisabled = runMenu.IsSubMenuOpen && !StartItem().IsEffectivelyEnabled;
            check(emptyDisabled, "Menu: “Iniciar” fica apagado quando o fluxo está vazio",
                $"aberto={runMenu.IsSubMenuOpen} · texto=“{StartItem().Header}”");
            runMenu.IsSubMenuOpen = false;
            await UiProbe.Pump(200);

            vm.Editor!.InsertSteps([new WaitStep { Ms = 4000 }]);
            await UiProbe.Pump(200);
            await UiProbe.ClickAsync(runMenu, 300);
            var r4 = await UiProbe.ClickAsync(StartItem(), 400);
            check(vm.Runner.IsBusy, "Menu: Executar → Iniciar roda o fluxo", $"{r4} · rodando={vm.Runner.IsBusy}");
            vm.Runner.Kill();
            vm.Runner.Join(TimeSpan.FromSeconds(3));
            await UiProbe.Pump(400);

            vm.IsSettingsOpen = true;
            await UiProbe.Pump(500);
            var editor = w.GetVisualDescendants().OfType<HotkeyEditor>().FirstOrDefault(e => e.Name == "StartKeys");
            if (editor == null)
            {
                check(false, "Atalhos: campo para mudar a tecla", "campo não encontrado");
            }
            else
            {
                var hwnd = UiProbe.HwndOf(w);
                async Task Press(int vk, int scan)
                {
                    PostMessageW(hwnd, WM_KEYDOWN, new IntPtr(vk), new IntPtr(1 | (scan << 16)));
                    PostMessageW(hwnd, WM_KEYUP, new IntPtr(vk), new IntPtr(unchecked((int)(1 | (scan << 16) | 0xC0000000))));
                    await UiProbe.Pump(300);
                }

                var chip = editor.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible);
                var r3 = await UiProbe.ClickAsync(chip, 250);
                await Press(0x7A, 0x57);
                check(settings.Current.Hotkeys.Start == "F11", "Atalhos: clicar na tecla e apertar outra troca o atalho",
                    $"{r3} · Iniciar agora = “{settings.Current.Hotkeys.Start}”");

                var add = editor.GetVisualDescendants().OfType<Button>().Last(b => b.IsEffectivelyVisible);
                await UiProbe.ClickAsync(add, 250);
                await Press(0x77, 0x42);
                check(settings.Current.Hotkeys.Start == "F11, F8", "Atalhos: “+ Adicionar” dá outra tecla para a mesma ação",
                    $"Iniciar agora = “{settings.Current.Hotkeys.Start}”");
            }
        }
        catch (Exception ex)
        {
            check(false, "Janela principal: cliques", ex.Message);
        }
        finally
        {
            vm?.Shutdown();
            w?.Close();
            AppPaths.UseRoot(originalRoot);
        }
    }
}
