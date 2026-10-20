using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacroMidas.Controls;
using MacroMidas.Models;
using MacroMidas.Services;
using MacroMidas.ViewModels;
using MacroMidas.Views;
using MacroMidas.Views.Overlay;

namespace MacroMidas.Tests;

public class UiTests
{
    private static string ShotsDir
    {
        get
        {
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "screenshots"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static (MainWindow Window, MainWindowViewModel Vm) CreateMain(bool firstRun = false)
    {
        if (firstRun) TestData.UseFreshRoot();
        else TestData.UseTempRoot();
        var settings = new SettingsService();
        settings.Load();
        var window = new MainWindow { Width = 1340, Height = 860 };
        var vm = new MainWindowViewModel(settings, new FlowLibrary(), window, systemIntegration: false);
        window.DataContext = vm;
        window.Show();
        Settle();
        return (window, vm);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(40);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(TopLevel top, string name)
    {
        Settle();
        var frame = top.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(Path.Combine(ShotsDir, name + ".png"));
    }

    [AvaloniaFact]
    public void Editor_with_nested_blocks_renders()
    {
        var (w, vm) = CreateMain();
        vm.NewFlowCommand.Execute(null);
        var editor = vm.Editor!;
        editor.Flow.Name = "Farm de recursos";
        editor.Flow.Settings.RepeatMode = RepeatMode.Infinite;
        editor.SelectedStep = null;
        var pixelIf = new IfStep { Condition = new Condition { Kind = ConditionKind.PixelColor, X = 960, Y = 540, Color = "#FFD54A" } };
        pixelIf.Then.Add(new ClickStep { X = 960, Y = 540 });
        pixelIf.Then.Add(new WaitStep { Ms = 300 });
        pixelIf.Else.Add(new WaitStep { Ms = 200 });
        editor.InsertSteps([pixelIf]);
        editor.SelectedStep = null;
        editor.InsertSteps(
        [
            new ClickStep { X = 812, Y = 455, Note = "Abrir inventário" },
            new WaitStep { Ms = 350, Random = true, MaxMs = 700 },
            new DragStep { FromX = 640, FromY = 380, ToX = 1100, ToY = 380 },
            new KeyStep { Keys = "Ctrl+Shift+S" },
            new ScrollStep { Direction = ScrollDirection.Down, Amount = 5, UsePosition = true, X = 700, Y = 420, ClickFirst = true },
            new MousePathStep { Path = [0, 100, 100, 8, 140, 120, 8, 190, 150, 8, 260, 170] },
            new TypeTextStep { Text = "Olá, mundo! ✨" },
        ]);
        var ifStep = editor.Flow.Steps.OfType<IfStep>().First();
        editor.SelectedStep = ifStep;
        Shot(w, "01-editor-if");

        editor.SelectedStep = editor.Flow.Steps.OfType<ClickStep>().First();
        editor.Flow.Steps.OfType<WaitStep>().First().IsRunning = true;
        Shot(w, "02-editor-click");
    }

    [AvaloniaFact]
    public void Inspector_number_boxes_update_the_model()
    {
        var (w, vm) = CreateMain();
        vm.NewFlowCommand.Execute(null);
        var click = new ClickStep { HoldMs = 2000 };
        vm.Editor!.InsertSteps([click]);
        vm.Editor.SelectedStep = click;
        Settle();

        var boxes = w.GetVisualDescendants().OfType<NumericUpDown>().ToList();
        Assert.NotEmpty(boxes);
        var hold = boxes.First(b => b.Value == 2);
        hold.Value = 3;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3000, click.HoldMs);

        var radios = w.GetVisualDescendants().OfType<RadioButton>().Where(r => (r.Content as string) == "Duplo").ToList();
        Assert.Single(radios);
        radios[0].IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, click.Clicks);
    }

    [AvaloniaFact]
    public void Drag_and_drop_reorders_and_moves_into_blocks()
    {
        var (w, vm) = CreateMain();
        vm.NewFlowCommand.Execute(null);
        var editor = vm.Editor!;
        var a = new KeyStep { Keys = "A" };
        var b = new KeyStep { Keys = "B" };
        var c = new KeyStep { Keys = "C" };
        var block = new RepeatStep { Count = 2 };
        editor.InsertSteps([a, b, c, block]);
        editor.SelectedStep = null;
        Settle();

        Point CenterOf(FlowStep s, double dy = 0)
        {
            var card = w.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("stepCard") && ReferenceEquals(x.DataContext, s));
            return card.TranslatePoint(new Point(60, card.Bounds.Height / 2 + dy), w)!.Value;
        }

        var from = CenterOf(a);
        var to = CenterOf(c, 8);
        w.MouseDown(from, Avalonia.Input.MouseButton.Left);
        w.MouseMove(new Point(from.X, from.Y + 12));
        w.MouseMove(to);
        w.MouseUp(to, Avalonia.Input.MouseButton.Left);
        Settle();
        Assert.Equal([b, c, a, block], editor.Flow.Steps.ToArray());

        var addButton = w.GetVisualDescendants().OfType<AddStepButton>().First(x => ReferenceEquals(x.TargetList, block.Body));
        var target = addButton.TranslatePoint(new Point(10, addButton.Bounds.Height / 2), w)!.Value;
        from = CenterOf(b);
        w.MouseDown(from, Avalonia.Input.MouseButton.Left);
        w.MouseMove(new Point(from.X, from.Y + 12));
        w.MouseMove(target);
        w.MouseUp(target, Avalonia.Input.MouseButton.Left);
        Settle();
        Assert.Equal([c, a, block], editor.Flow.Steps.ToArray());
        Assert.Equal([b], block.Body.ToArray());

        editor.UndoCommand.Execute(null);
        Assert.Equal(4, editor.Flow.Steps.Count);
        Assert.Empty(editor.Flow.Steps.OfType<RepeatStep>().Single().Body);
    }

    [AvaloniaFact]
    public void Settings_and_home_render()
    {
        var (w, vm) = CreateMain();
        vm.IsSettingsOpen = true;
        Shot(w, "03-settings");
        w.Close();

        AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "MacroMidasTests", "home-" + Guid.NewGuid().ToString("N")));
        AppPaths.Ensure();
        var settings = new SettingsService();
        settings.Load();
        settings.Current.ExamplesSeeded = true;
        var home = new MainWindow { Width = 1340, Height = 860 };
        home.DataContext = new MainWindowViewModel(settings, new FlowLibrary(), home, systemIntegration: false);
        home.Show();
        Shot(home, "04-home");
        TestData.UseTempRoot();
    }

    [AvaloniaFact]
    public void Dialog_and_toasts_render()
    {
        var (w, vm) = CreateMain();
        vm.Toast("Fluxo exportado com sucesso", ToastKind.Success);
        vm.Toast("Janela alvo roda como administrador", ToastKind.Warning);
        _ = vm.ConfirmAsync("Apagar fluxo", "“Exemplo · Auto-clique” será apagado permanentemente.", "Apagar", danger: true);
        Shot(w, "05-dialog");
        vm.Dialog?.CancelCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Control_bar_renders()
    {
        var bar = new ControlBarViewModel
        {
            Title = "Farm de recursos",
            Status = "Ciclo 3/10 · Clique",
            IsLock = true,
            PauseCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            StopCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            StopRecordingCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            CancelRecordingCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }),
            RestOpacity = 1
        };
        var win = new ControlBarWindow { DataContext = bar, Background = Avalonia.Media.Brushes.Black };
        win.Show();
        Shot(win, "06-controlbar");
        bar.StopPending = true;
        bar.IsPaused = true;
        Shot(win, "07-controlbar-stopping");
        bar.IsRecording = true;
        bar.Title = "Gravando · Novo fluxo";
        bar.Status = "12 evento(s) · F9 ou Concluir para terminar";
        Shot(win, "08-controlbar-recording");
        win.Close();
    }

    [AvaloniaFact]
    public void Menu_bar_has_the_classic_menus()
    {
        var (w, vm) = CreateMain(firstRun: true);
        Assert.Equal(["Auto-clique"], vm.Flows.Select(f => f.Name).ToArray());

        var menu = w.GetVisualDescendants().OfType<Menu>().Single();
        var top = menu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(["Arquivo", "Editar", "Executar", "Opções", "Ajuda"], top.Select(m => (string)m.Header!).ToArray());

        MenuItem Find(MenuItem parent, string header) => parent.Items.OfType<MenuItem>().First(m => ((string)m.Header!).StartsWith(header));

        Assert.DoesNotContain(top[0].Items.OfType<MenuItem>(), m => ((string)m.Header!).Contains("modelo", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(top[2].Items.OfType<MenuItem>(), m => ((string)m.Header!).Contains("Modo Lock"));
        var repeat = top[2].Items.OfType<MenuItem>().First(m => ((string)m.Header!).StartsWith("Repetir sem parar"));
        repeat.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RepeatMode.Infinite, vm.SelectedFlow!.Settings.RepeatMode);
        repeat.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RepeatMode.Count, vm.SelectedFlow.Settings.RepeatMode);

        var topmost = Find(top[3], "Sempre no topo");
        topmost.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Settings.AlwaysOnTop);
        Assert.True(w.Topmost);
        topmost.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Shot(w, "00-main");
        ShotMenu(w, top[0], "00-menu-arquivo");
        ShotMenu(w, top[2], "00-menu-executar");

        var editorView = w.GetVisualDescendants().OfType<FlowEditorView>().Single();
        editorView.OpenRunSettings();
        Settle();
        var runButton = editorView.FindControl<Button>("RunSettingsButton")!;
        var host = ((IPopupHostProvider)runButton.Flyout!).PopupHost;
        Assert.NotNull(host);
        Shot(host as TopLevel ?? w, "11-run-settings");
        runButton.Flyout!.Hide();
    }

    private static void ShotMenu(TopLevel window, MenuItem item, string name)
    {
        item.IsSubMenuOpen = true;
        Settle();
        var popup = item.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
        Assert.True(popup?.IsOpen == true, $"menu “{item.Header}” não abriu");
        Shot(popup!.Host as TopLevel ?? window, name);
        item.IsSubMenuOpen = false;
        Settle();
    }

    [AvaloniaFact]
    public void Old_examples_are_cleaned_and_every_flow_goes_to_lock()
    {
        FlowLibrary library = null!;
        Flow Saved(string example, string name, bool edit = false)
        {
            var f = new Flow();
            switch (example)
            {
                case "pixel-if":
                    var ifStep = new IfStep { Condition = new Condition { Kind = ConditionKind.PixelColor, X = 960, Y = 540, Color = "#FFD54A" } };
                    ifStep.Then.Add(new ClickStep { X = 960, Y = 540 });
                    ifStep.Then.Add(new WaitStep { Ms = 300 });
                    ifStep.Else.Add(new WaitStep { Ms = 200 });
                    f.Steps.Add(ifStep);
                    break;
                case "drag":
                    f.Steps.Add(new DragStep { FromX = 500, FromY = 500, ToX = 900, ToY = 500, DurationMs = 600 });
                    f.Steps.Add(new WaitStep { Ms = 400 });
                    break;
                default:
                    f = FlowTemplates.AutoClick();
                    break;
            }
            f.Name = name;
            f.Settings.InputMode = InputMode.Direct;
            foreach (var step in Flow.Walk(f.Steps)) step.Note = "nota antiga";
            if (edit) f.Steps.Add(new KeyStep { Keys = "Space" });
            library.Save(f);
            return f;
        }
        MainWindowViewModel Open(out SettingsService settings)
        {
            settings = new SettingsService();
            settings.Load();
            settings.Current.ExamplesSeeded = true;
            return new MainWindowViewModel(settings, new FlowLibrary(), null, systemIntegration: false);
        }

        TestData.UseFreshRoot();
        library = new FlowLibrary();
        Saved("pixel-if", "Exemplo · Clique por cor");
        Saved("drag", "Exemplo · Arrastar", edit: true);
        Saved("autoclick", "Exemplo · Auto-clique");
        Saved("drag", "Meu fluxo");
        var vm = Open(out var s1);
        Assert.Equal(["Auto-clique", "Exemplo · Arrastar", "Meu fluxo"], vm.Flows.Select(f => f.Name).OrderBy(n => n).ToArray());
        Assert.All(vm.Flows, f => Assert.Equal(InputMode.Background, f.Settings.InputMode));
        Assert.All(Flow.Walk(vm.Flows.First(f => f.Name == "Auto-clique").Steps), st => Assert.Null(st.Note));
        Assert.Equal(3, Directory.GetFiles(AppPaths.Flows).Length);
        Assert.True(s1.Current.ExamplesCleaned);
        Assert.All(new FlowLibrary().LoadAll(), f => Assert.Equal(InputMode.Background, f.Settings.InputMode));

        TestData.UseFreshRoot();
        library = new FlowLibrary();
        Saved("autoclick", "Auto-clique", edit: true);
        Saved("autoclick", "Exemplo · Auto-clique");
        vm = Open(out _);
        Assert.Equal(["Auto-clique"], vm.Flows.Select(f => f.Name).ToArray());
        Assert.Equal(3, vm.Flows[0].Steps.Count);
        TestData.UseTempRoot();
    }

    [AvaloniaFact]
    public void Deleted_flow_does_not_come_back_and_start_needs_steps()
    {
        var (_, vm) = CreateMain(firstRun: true);
        vm.Settings.ConfirmDelete = false;
        vm.NewFlowCommand.Execute(null);
        var flow = vm.SelectedFlow!;
        Assert.False(vm.CanStart);
        Assert.Equal("Iniciar (adicione passos primeiro)", vm.StartMenuText);
        vm.Editor!.InsertSteps([new WaitStep { Ms = 10 }]);
        Assert.True(vm.CanStart);
        Assert.Equal("Iniciar", vm.StartMenuText);

        var path = Path.Combine(AppPaths.Flows, $"{flow.Id:N}.mmf");
        Assert.True(File.Exists(path));
        vm.DeleteFlowCommand.Execute(null);
        Settle();
        Assert.DoesNotContain(flow, vm.Flows);
        Assert.False(File.Exists(path));
        TestData.UseTempRoot();
    }

    [AvaloniaFact]
    public void Toggling_proportional_converts_coords_and_undo_restores()
    {
        var (_, vm) = CreateMain();
        vm.NewFlowCommand.Execute(null);
        var editor = vm.Editor!;
        editor.InsertSteps([new ClickStep { X = 400, Y = 300 }]);
        editor.TrackChanges();
        var (pw, ph) = MacroMidas.Native.Win32.PrimaryScreenSize();

        editor.Flow.Settings.CoordinateMode = CoordinateMode.Proportional;
        Dispatcher.UIThread.RunJobs();
        var click = editor.Flow.Steps.OfType<ClickStep>().First();
        Assert.Equal((int)Math.Round(400.0 * 1000 / pw), click.X);
        Assert.Equal((int)Math.Round(300.0 * 1000 / ph), click.Y);

        editor.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CoordinateMode.Pixels, editor.Flow.Settings.CoordinateMode);
        Assert.Equal((400, 300), (editor.Flow.Steps.OfType<ClickStep>().First().X, editor.Flow.Steps.OfType<ClickStep>().First().Y));
    }

    [AvaloniaFact]
    public void File_menu_actions_are_enabled_and_work()
    {
        var (w, vm) = CreateMain();
        Assert.NotNull(vm.SelectedFlow);
        var file = w.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().First();
        file.IsSubMenuOpen = true;
        Settle();

        MenuItem Item(string header) => file.Items.OfType<MenuItem>().First(m => ((string)m.Header!).StartsWith(header));
        foreach (var h in new[] { "Exportar (.mmf)", "Exportar como texto", "Duplicar fluxo", "Apagar fluxo" })
            Assert.True(Item(h).IsEffectivelyEnabled, $"“{h}” está desabilitado com um fluxo selecionado");

        var dup = Item("Duplicar fluxo");
        var n = vm.Flows.Count;
        dup.Command!.Execute(dup.CommandParameter);
        Settle();
        Assert.Equal(n + 1, vm.Flows.Count);
        file.IsSubMenuOpen = false;
    }

    [AvaloniaFact]
    public void Add_button_inside_a_block_opens_its_menu()
    {
        var (w, vm) = CreateMain();
        vm.NewFlowCommand.Execute(null);
        var editor = vm.Editor!;
        var repeat = new RepeatStep();
        editor.InsertSteps([repeat]);
        editor.SelectedStep = null;
        Settle();

        var add = w.GetVisualDescendants().OfType<AddStepButton>().First(x => ReferenceEquals(x.TargetList, repeat.Body));
        var p = add.TranslatePoint(new Point(14, add.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, Avalonia.Input.MouseButton.Left);
        w.MouseUp(p, Avalonia.Input.MouseButton.Left);
        Settle();

        Assert.True(add.OpenedFlyout?.IsOpen == true, "o “+ adicionar” do bloco não abriu o menu");
        var options = (add.OpenedFlyout!.Content as Panel)!.Children.OfType<Button>().ToList();
        Assert.NotEmpty(options);
        add.OpenedFlyout!.Hide();
    }

    [AvaloniaFact]
    public void Parallel_card_shows_tracks_and_track_buttons_work()
    {
        var (w, vm) = CreateMain();
        vm.NewFlowCommand.Execute(null);
        var editor = vm.Editor!;
        var parallel = new ParallelStep();
        parallel.Tracks[0].Steps.Add(new KeyStep { Keys = "W", Action = KeyAction.Down });
        parallel.Tracks[1].Steps.Add(new ClickStep { X = 10, Y = 10 });
        editor.InsertSteps([parallel]);
        editor.SelectedStep = parallel;
        Shot(w, "14-parallel");

        var adders = w.GetVisualDescendants().OfType<AddStepButton>()
            .Where(x => parallel.Tracks.Any(t => ReferenceEquals(x.TargetList, t.Steps))).ToList();
        Assert.Equal(2, adders.Count);

        string? Tip(Control b) => Avalonia.Controls.ToolTip.GetTip(b) as string;

        var removeBefore = w.GetVisualDescendants().OfType<Button>().Where(b => Tip(b) == "Remover esta trilha").ToList();
        Assert.All(removeBefore, b => Assert.False(b.IsVisible));

        var addTrack = w.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "+ trilha");
        Assert.NotNull(addTrack.Command);
        addTrack.Command!.Execute(addTrack.CommandParameter);
        Settle();
        Assert.Equal(3, parallel.TrackCount);

        var removers = w.GetVisualDescendants().OfType<Button>().Where(b => Tip(b) == "Remover esta trilha").ToList();
        Assert.Equal(3, removers.Count);
        Assert.All(removers, b => Assert.True(b.IsVisible));
        Assert.All(removers, b => Assert.NotNull(b.Command));
        removers[0].Command!.Execute(removers[0].CommandParameter);
        Settle();
        Assert.Equal(2, parallel.TrackCount);
    }

    [AvaloniaFact]
    public void Hotkeys_can_be_changed_added_and_removed()
    {
        var (w, vm) = CreateMain();
        vm.Settings.Hotkeys.Start = "F6";
        vm.IsSettingsOpen = true;
        Settle();
        var editor = w.GetVisualDescendants().OfType<HotkeyEditor>().First(e => e.Name == "StartKeys");
        var captures = new List<bool>();
        void OnCapture(bool on) => captures.Add(on);
        KeyCapture.StateChanged += OnCapture;
        try
        {
            editor.StartCapture(0);
            Assert.True(editor.IsCapturing);
            w.KeyPress(Avalonia.Input.Key.F11, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.F11, null);
            Assert.Equal("F11", vm.Settings.Hotkeys.Start);

            editor.StartCapture(1);
            w.KeyPress(Avalonia.Input.Key.D1, Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Alt, Avalonia.Input.PhysicalKey.Digit1, null);
            Assert.Equal("F11, Ctrl+Alt+1", vm.Settings.Hotkeys.Start);
            Settle();
            Shot(w, "12-hotkeys");

            editor.StartCapture(0);
            w.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
            Assert.Equal("F11, Ctrl+Alt+1", vm.Settings.Hotkeys.Start);
            Assert.False(editor.IsCapturing);
            Assert.Equal([true, false, true, false, true, false], captures.ToArray());
        }
        finally
        {
            KeyCapture.StateChanged -= OnCapture;
            vm.Settings.Hotkeys.Start = "F6";
        }
    }

    [AvaloniaFact]
    public void Lock_frame_renders()
    {
        var w = new LockFrameWindow { Width = 640, Height = 380, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A2D33")) };
        w.Configure(Avalonia.Media.Color.Parse("#FFC83D"), blocksMouse: true);
        w.Show();
        Shot(w, "10-lockframe");
        w.Close();
    }

    [AvaloniaFact]
    public void Pulse_overlay_renders()
    {
        var w = new PulseOverlayWindow { Width = 900, Height = 520, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#202024")) };
        w.Configure(Avalonia.Media.Color.Parse("#FFC83D"), 0.9, 3, 26);
        w.Show();
        Shot(w, "09-overlay");
        w.Close();
    }

    [AvaloniaFact]
    public void Countdown_overlay_renders()
    {
        var w = new CountdownOverlayWindow { Width = 900, Height = 520, Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#202024")) };
        w.Set(3, "Gravando em…");
        w.Show();
        Shot(w, "13-countdown");
        w.Close();
    }
}
