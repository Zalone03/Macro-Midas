using Avalonia.Media;
using Avalonia.Threading;
using MacroMidas.Capture;
using MacroMidas.Engine;
using MacroMidas.Hooks;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Native;
using MacroMidas.ViewModels;
using MacroMidas.Views.Overlay;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Diagnostics;

// Autoteste do que depende do Windows de verdade: sobreposições, Lock, pixels, hooks e atalhos.
public static partial class SelfTest
{
    public static async Task<int> RunAsync(string reportPath)
    {
        var lines = new List<string> { $"Macro Midas: autoteste {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "" };
        var failures = 0;

        void Check(bool ok, string name, string detail = "")
        {
            lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  : " + detail : "")}");
            if (!ok) failures++;
        }

        var awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
        Check(awareness == 2, "DPI por monitor (coordenadas em pixels físicos)", $"awareness={awareness}");

        await TestOverlay(Check);
        await TestControlBar(Check);
        await TestLock(Check);
        await TestScreenLock(Check);
        await TestDrag(Check);
        await TestLockScroll(Check);
        await TestPathReplay(Check);
        await TestBarClicks(Check);
        await TestMainWindowClicks(Check);

        try
        {
            using var hk = new HotkeyService(a => Dispatcher.UIThread.Post(a));
            var failed = await hk.SetBindingsAsync([new HotkeyService.Binding("teste", "Ctrl+Alt+Shift+F12", () => { })]);
            Check(failed.Count == 0, "Atalhos globais (RegisterHotKey)", string.Join(", ", failed));
        }
        catch (Exception ex)
        {
            Check(false, "Atalhos globais", ex.Message);
        }

        try
        {
            using var hook = new LowLevelInputHook(mouse: true, keyboard: true);
            Check(hook.Start(), "Hooks globais de mouse e teclado (gravação / failsafe)");
        }
        catch (Exception ex)
        {
            Check(false, "Hooks globais", ex.Message);
        }

        try
        {
            using var raw = new RawMouseInput(System.Diagnostics.Stopwatch.StartNew());
            Check(raw.Start(), "Gravação: movimento do mouse em jogos com o cursor preso (Raw Input)");
        }
        catch (Exception ex)
        {
            Check(false, "Raw Input", ex.Message);
        }

        lines.Add("");
        lines.Add(failures == 0 ? "RESULTADO: tudo certo" : $"RESULTADO: {failures} falha(s)");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllLinesAsync(reportPath, lines);
        return failures;
    }

    private static bool IsGoldish(RgbColor c) => c.R > 150 && c.G > 90 && c.B < c.R - 40;

    private static async Task TestOverlay(Action<bool, string, string> check)
    {
        PulseOverlayWindow? overlay = null;
        try
        {
            overlay = new PulseOverlayWindow();
            var screen = overlay.Screens.Primary ?? overlay.Screens.All[0];
            overlay.Configure(Color.Parse("#FFC83D"), 1.0, 8, 30);
            overlay.PlaceOn(screen.Bounds);
            overlay.Show();
            await Task.Delay(800);

            var h = overlay.Hwnd;
            var ex = GetWindowLong(h, GWL_EXSTYLE);
            check((ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_LAYERED) != 0, "Borda: o mouse atravessa", $"exstyle=0x{ex:X}");
            check((ex & WS_EX_NOACTIVATE) != 0 && (ex & WS_EX_TOOLWINDOW) != 0, "Borda: não rouba foco nem aparece na barra de tarefas", "");
            GetWindowDisplayAffinity(h, out var aff);
            check(aff == WDA_EXCLUDEFROMCAPTURE, "Borda: marcada como invisível para capturas", $"affinity=0x{aff:X}");

            GetWindowRect(h, out var wr);
            check(wr.Left == screen.Bounds.X && wr.Top == screen.Bounds.Y && wr.Width == screen.Bounds.Width && wr.Height == screen.Bounds.Height,
                "Borda: cobre o monitor inteiro", $"janela=({wr.Left},{wr.Top} {wr.Width}×{wr.Height}) monitor={screen.Bounds}");

            var px = screen.Bounds.X + 3;
            var py = screen.Bounds.Y + screen.Bounds.Height / 2;
            var hit = GetAncestor(WindowFromPoint(new POINT(px, py)), GA_ROOT);
            check(hit != h, "Borda: o clique vai para a janela de baixo", $"janela sob o ponto=0x{hit:X}");

            SetWindowDisplayAffinity(h, WDA_NONE);
            await Task.Delay(500);
            var visible = GdiCapture.ScreenPixel(px, py);
            SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE);
            await Task.Delay(500);
            var excluded = GdiCapture.ScreenPixel(px, py);
            overlay.Close();
            overlay = null;
            await Task.Delay(400);
            var without = GdiCapture.ScreenPixel(px, py);

            check(visible is { } v && IsGoldish(v), "Borda: aparece dourada na tela", $"pixel visível={visible?.ToHex()}");
            var notCaptured = excluded is { } ex2 && visible is { } vis && without is { } wo && ex2.DistanceTo(wo) < ex2.DistanceTo(vis);
            check(notCaptured, "Borda: não interfere na leitura de pixels",
                $"leitura com borda={excluded?.ToHex()} · sem borda={without?.ToHex()} · borda={visible?.ToHex()}");
        }
        catch (Exception ex)
        {
            check(false, "Borda dourada", ex.Message);
        }
        finally
        {
            overlay?.Close();
        }
    }

    private static async Task TestControlBar(Action<bool, string, string> check)
    {
        try
        {
            var vm = new ControlBarViewModel
            {
                Title = "Autoteste",
                Status = "Verificando a barra",
                PauseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                StopCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                StopRecordingCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
                CancelRecordingCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            };
            var bar = new ControlBarWindow { DataContext = vm };
            bar.Show();
            await Task.Delay(500);
            var h = bar.Hwnd;
            var ex = GetWindowLong(h, GWL_EXSTYLE);
            check((ex & WS_EX_NOACTIVATE) != 0 && (ex & WS_EX_TRANSPARENT) == 0, "Barra: clicável e sem roubar o foco", $"exstyle=0x{ex:X}");
            GetWindowDisplayAffinity(h, out var aff);
            check(aff == WDA_EXCLUDEFROMCAPTURE, "Barra: invisível para leitura de pixels", $"affinity=0x{aff:X}");
            GetWindowRect(h, out var r);
            var screen = bar.Screens.Primary!;
            var centered = Math.Abs((r.Left + r.Width / 2) - (screen.WorkingArea.X + screen.WorkingArea.Width / 2)) <= 2;
            check(centered && r.Bottom <= screen.WorkingArea.Bottom + 1 && r.Bottom >= screen.WorkingArea.Bottom - 2,
                "Barra: centralizada embaixo da tela", $"rect=({r.Left},{r.Top},{r.Right},{r.Bottom}) área={screen.WorkingArea}");
            bar.Close();
        }
        catch (Exception ex)
        {
            check(false, "Barra de controle", ex.Message);
        }
    }

    // Arrastar: segura o botão e move; numa caixa de texto isso seleciona o texto.
    private static async Task TestDrag(Action<bool, string, string> check)
    {
        var top = IntPtr.Zero;
        try
        {
            top = CreateWindowExW(0, "STATIC", "Autoteste · arrastar", (uint)WS_OVERLAPPEDWINDOW,
                160, 160, 480, 150, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var edit = CreateWindowExW(0, "EDIT", "", (uint)(WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL),
                10, 10, 440, 40, top, new IntPtr(1), IntPtr.Zero, IntPtr.Zero);
            if (top == IntPtr.Zero || edit == IntPtr.Zero)
            {
                check(false, "Arrastar: janela de teste criada", "");
                return;
            }
            SetWindowTextW(edit, new string('W', 80));
            ShowWindow(top, SW_SHOWNOACTIVATE);
            await Task.Delay(300);
            SendMessageW(edit, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);

            var flow = new Flow { Name = "autoteste-arrastar" };
            flow.Steps.Add(new DragStep { Button = MouseButtonKind.Left, FromX = 20, FromY = 20, ToX = 320, ToY = 20, DurationMs = 120, HoldMs = 20 });
            var env = new RunEnvironment { Input = new WindowMessageBackend(top), Pixels = new WindowPixelReader(top) };
            var runner = new FlowRunner();
            var done = new TaskCompletionSource<RunFinishedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            runner.Finished += i => done.TrySetResult(i);
            runner.Start(flow, env, new RunOptions { HighPrecisionTimer = false, SkipStartDelay = true });
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(300);

            var sel = (uint)SendMessageW(edit, EM_GETSEL, IntPtr.Zero, IntPtr.Zero);
            uint start = sel & 0xFFFF, end = (sel >> 16) & 0xFFFF;
            check(end > start, "Arrastar: botão segurado + movimento selecionam texto na janela", $"seleção {start}..{end}");
        }
        catch (Exception ex)
        {
            check(false, "Arrastar", ex.Message);
        }
        finally
        {
            if (top != IntPtr.Zero) DestroyWindow(top);
        }
    }

    // Lock na tela inteira: entrada vai para a janela sob o ponto, nunca para o próprio app.
    private static async Task TestScreenLock(Action<bool, string, string> check)
    {
        var edit = IntPtr.Zero;
        try
        {
            edit = CreateWindowExW(0, "EDIT", "", (uint)(WS_OVERLAPPEDWINDOW | ES_MULTILINE | ES_AUTOVSCROLL),
                140, 140, 480, 220, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (edit == IntPtr.Zero)
            {
                check(false, "Lock na tela: janela de teste criada", "");
                return;
            }
            ShowWindow(edit, SW_SHOWNOACTIVATE);
            await Task.Delay(400);

            var pt = new POINT(40, 40);
            ClientToScreen(edit, ref pt);
            var hit = WindowFinder.HitTestTopLevel(pt.X, pt.Y);
            check(hit != edit, "Lock na tela: cliques nunca caem numa janela do Macro Midas", $"janela sob o ponto=0x{hit:X}");

            var injected = 0;
            using var spy = new LowLevelInputHook(mouse: true, keyboard: true);
            spy.Event += e =>
            {
                if (e.Injected && e.FromMacroMidas) Interlocked.Increment(ref injected);
            };
            spy.Start();

            var flow = new Flow { Name = "autoteste-lock-tela" };
            flow.Steps.Add(new ClickStep { X = pt.X, Y = pt.Y, HoldMs = 10 });
            flow.Steps.Add(new TypeTextStep { Text = "tela inteira ✓", CharDelayMs = 2 });

            var env = new RunEnvironment
            {
                Input = new ScreenMessageBackend(h => h != edit),
                Pixels = new ScreenPixelReader(IntPtr.Zero),
                TargetDescription = "tela inteira (teste)"
            };
            var runner = new FlowRunner();
            var done = new TaskCompletionSource<RunFinishedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            runner.Finished += i => done.TrySetResult(i);
            runner.Start(flow, env, new RunOptions { HighPrecisionTimer = false, SkipStartDelay = true });
            var info = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(400);

            var text = WindowFinder.GetTitle(edit);
            spy.Stop();
            check(info.Reason == RunEndReason.Completed && text.Contains("tela inteira ✓"),
                "Lock na tela: clique e texto chegaram na janela sob o ponto", $"conteúdo=“{text}”");
            check(injected == 0, "Lock na tela: o mouse e o teclado reais não foram usados", $"entradas simuladas detectadas={injected}");
        }
        catch (Exception ex)
        {
            check(false, "Lock na tela", ex.Message);
        }
        finally
        {
            if (edit != IntPtr.Zero) DestroyWindow(edit);
        }
    }

    private static async Task TestLock(Action<bool, string, string> check)
    {
        var edit = IntPtr.Zero;
        try
        {
            edit = CreateWindowExW(0, "EDIT", "", (uint)(WS_OVERLAPPEDWINDOW | ES_MULTILINE | ES_AUTOVSCROLL),
                80, 80, 520, 260, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            check(edit != IntPtr.Zero, "Lock: janela de teste (EDIT nativo) criada", "");
            if (edit == IntPtr.Zero) return;
            ShowWindow(edit, SW_SHOWNOACTIVATE);
            await Task.Delay(400);

            var injected = 0;
            using var spy = new LowLevelInputHook(mouse: true, keyboard: true);
            spy.Event += e =>
            {
                if (e.Injected && e.FromMacroMidas) Interlocked.Increment(ref injected);
            };
            spy.Start();

            var flow = new Flow { Name = "autoteste-lock" };
            flow.Steps.Add(new ClickStep { X = 30, Y = 30, HoldMs = 10 });
            flow.Steps.Add(new TypeTextStep { Text = "Olá Midas ✨", CharDelayMs = 2 });
            flow.Steps.Add(new KeyStep { Keys = "Enter", HoldMs = 5 });
            flow.Steps.Add(new TypeTextStep { Text = "linha2", CharDelayMs = 2 });

            var env = new RunEnvironment
            {
                Input = new WindowMessageBackend(edit),
                Pixels = new WindowPixelReader(edit),
                TargetWindow = edit,
                TargetDescription = "EDIT de teste"
            };
            var runner = new FlowRunner();
            var done = new TaskCompletionSource<RunFinishedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            runner.Finished += i => done.TrySetResult(i);
            runner.Start(flow, env, new RunOptions { HighPrecisionTimer = false, SkipStartDelay = true });
            var info = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(400);

            var text = WindowFinder.GetTitle(edit);
            spy.Stop();
            check(info.Reason == RunEndReason.Completed, "Lock: fluxo executou até o fim", info.Reason.ToString());
            check(text.Contains("Olá Midas ✨"), "Lock: texto (com acento e emoji) chegou na janela em segundo plano",
                $"conteúdo=“{text.Replace("\r\n", "⏎")}”");
            check(text.Replace("\r\n", "\n").Contains("✨\nlinha2"), "Lock: tecla Enter chegou", "");
            check(injected == 0, "Lock: nada foi simulado no mouse/teclado reais do sistema",
                $"entradas simuladas detectadas={injected}");

            var px = new WindowPixelReader(edit).GetPixel(10, 200);
            check(px is { } p && p.R > 200 && p.G > 200 && p.B > 200, "Lock: leitura de pixel direto da janela (PrintWindow)", $"pixel={px?.ToHex()}");
        }
        catch (Exception ex)
        {
            check(false, "Lock", ex.Message);
        }
        finally
        {
            if (edit != IntPtr.Zero) DestroyWindow(edit);
        }
    }
}
