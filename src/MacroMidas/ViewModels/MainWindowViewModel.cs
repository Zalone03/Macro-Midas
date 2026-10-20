using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroMidas.Controls;
using MacroMidas.Engine;
using MacroMidas.Hooks;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Serialization;
using MacroMidas.Services;

namespace MacroMidas.ViewModels;

// Tela inicial quando ainda não há fluxo selecionado.
public sealed class HomeViewModel(MainWindowViewModel main)
{
    public MainWindowViewModel Main { get; } = main;
}

public partial class MainWindowViewModel : ObservableObject, IEditorHost
{
    private readonly SettingsService _settingsService;
    private readonly FlowLibrary _library;
    private readonly HotkeyService? _hotkeys;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly DispatcherTimer _uiTimer;
    private readonly Dictionary<Flow, FlowEditorViewModel> _editors = new(ReferenceEqualityComparer.Instance);
    private string _hotkeySignature = "";
    private bool _capturingKey;

    public FlowRunner Runner { get; } = new();
    public OverlayService? Overlays { get; }
    public PickerService? Picker { get; }
    public IWindowHost? Host { get; }
    public AppSettings Settings => _settingsService.Current;
    public SettingsViewModel SettingsPage { get; }
    public HomeViewModel Home { get; }
    public ControlBarViewModel Bar { get; }

    public ObservableCollection<Flow> Flows { get; } = [];
    public ObservableCollection<Flow> FilteredFlows { get; } = [];
    public ObservableCollection<RunLogEntry> Log { get; } = [];
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(HasFlow), nameof(CanStart), nameof(StartMenuText))]
    private Flow? _selectedFlow;

    [ObservableProperty] private Flow? _listSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(CanStart), nameof(StartMenuText))]
    private FlowEditorViewModel? _editor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private bool _isSettingsOpen;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private DialogViewModel? _dialog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsPaused), nameof(IsBusy), nameof(IsIdle), nameof(CanStart), nameof(StartMenuText))]
    private RunState _runState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(CanStart), nameof(RecordMenuText))]
    private bool _isRecording;

    [ObservableProperty] private string _statusText = "Pronto";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private string _saveState = "";
    [ObservableProperty] private bool _isLogOpen;

    public object CurrentPage => IsSettingsOpen ? SettingsPage : Editor != null ? Editor : Home;
    public bool HasFlow => SelectedFlow != null;
    public bool IsRunning => RunState != RunState.Idle;
    public bool IsPaused => RunState == RunState.Paused;
    public bool IsBusy => IsRunning || IsRecording;
    public bool IsIdle => !IsBusy;

    // Texto do item Iniciar: explica por que está apagado quando o fluxo está vazio.
    public string StartMenuText => IsPaused ? "Continuar" : Editor is { IsEmpty: true } ? "Iniciar (adicione passos primeiro)" : "Iniciar";

    public string RecordMenuText => IsRecording ? "Concluir gravação" : "Gravar";

    // Iniciar faz algo agora: o fluxo tem passos e nada está rodando (ou está pausado e retoma).
    public bool CanStart => HasFlow && (IsPaused || (!IsBusy && Editor is { IsEmpty: false }));

    public string HotkeyHint
    {
        get
        {
            var h = Settings.Hotkeys;
            return $"{HotkeyList.First(h.Start)} iniciar  ·  {HotkeyList.First(h.Pause)} pausar  ·  {HotkeyList.First(h.Stop)} parar (2× mata)  ·  {HotkeyList.First(h.Record)} gravar";
        }
    }

    public MainWindowViewModel(SettingsService settings, FlowLibrary library, IWindowHost? host, bool systemIntegration = true)
    {
        _settingsService = settings;
        _library = library;
        Host = host;
        Home = new HomeViewModel(this);

        Bar = new ControlBarViewModel
        {
            PauseCommand = TogglePauseCommand,
            StopCommand = StopCommand,
            StopRecordingCommand = new RelayCommand(() => StopRecording(keep: true, fromApp: true)),
            CancelRecordingCommand = new RelayCommand(() => StopRecording(keep: false)),
        };

        if (host != null)
        {
            Overlays = new OverlayService(host.Window, Bar);
            Picker = new PickerService(host.Window);
        }

        SettingsPage = new SettingsViewModel(settings,
            Overlays != null ? () => Overlays.PreviewAsync(Settings, TimeSpan.FromSeconds(3)) : null,
            Toast);
        SettingsPage.HotkeysChanged += () =>
        {
            OnPropertyChanged(nameof(HotkeyHint));
            RegisterHotkeys();
        };

        Runner.StateChanged += s => Dispatcher.UIThread.Post(() => HandleRunnerState(s));
        Runner.Finished += info => Dispatcher.UIThread.Post(() => OnRunFinished(info));

        _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background, (_, _) => OnUiTick());
        _autosaveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.2), DispatcherPriority.Background, (_, _) => Autosave());

        foreach (var f in library.LoadAll()) Flows.Add(f);
        if (Flows.Count == 0 && !Settings.ExamplesSeeded) SeedExamples();
        else UpgradeOldFlows();
        RecordedFlowsToRealInput();
        SyncFilter();
        SelectedFlow = Flows.FirstOrDefault(f => f.Id.ToString() == Settings.LastFlowId) ?? Flows.FirstOrDefault();

        if (systemIntegration)
        {
            _hotkeys = new HotkeyService(a => Dispatcher.UIThread.Post(a));
            RegisterHotkeys(force: true);
            KeyCapture.StateChanged += OnKeyCaptureStateChanged;
        }
        _autosaveTimer.Start();
    }

    // Primeira vez: só um Auto-clique pronto para usar (sem fluxos de exemplo).
    private void SeedExamples()
    {
        var f = FlowTemplates.AutoClick();
        Flows.Add(f);
        _library.Save(f);
        Settings.ExamplesSeeded = true;
        Settings.ExamplesCleaned = true;
        _settingsService.Save();
    }

    // Migração única: tira exemplos intocados e passa todos os fluxos para o modo Lock.
    private void UpgradeOldFlows()
    {
        if (Settings.ExamplesCleaned) return;
        Settings.ExamplesCleaned = true;

        static string Shape(IEnumerable<FlowStep> steps) => string.Join("|", Flow.Walk(steps).Select(s => s.Summary));

        var examples = new (string Name, string Shape)[]
        {
            ("Exemplo · Clique por cor", "Se o ponto 960, 540 estiver com a cor escolhida|Clicar com o botão esquerdo em 960, 540|Esperar 0,3 segundo|Esperar 0,2 segundo"),
            ("Exemplo · Arrastar", "Arrastar de 500, 500 até 900, 500|Esperar 0,4 segundo"),
            ("Exemplo · Auto-clique", Shape(FlowTemplates.AutoClick().Steps)),
        };

        var changed = new HashSet<Flow>(ReferenceEqualityComparer.Instance);
        foreach (var (oldName, shape) in examples)
        {
            var flow = Flows.FirstOrDefault(f => f.Name == oldName);
            if (flow == null || Shape(flow.Steps) != shape) continue;

            if (oldName == "Exemplo · Auto-clique" && Flows.All(f => f.Name != "Auto-clique"))
            {
                flow.Name = "Auto-clique";
                foreach (var step in Flow.Walk(flow.Steps)) step.Note = null;
                changed.Add(flow);
            }
            else
            {
                Flows.Remove(flow);
                _library.Delete(flow);
            }
        }

        foreach (var flow in Flows.Where(f => f.Settings.InputMode != InputMode.Background))
        {
            flow.Settings.InputMode = InputMode.Background;
            changed.Add(flow);
        }
        foreach (var flow in changed) _library.Save(flow);
        _settingsService.Save();
    }

    // Fluxos gravados usam o mouse de verdade, porque jogos como o Roblox ignoram o Lock.
    private void RecordedFlowsToRealInput()
    {
        if (Settings.RecordedFlowsUseRealInput) return;
        Settings.RecordedFlowsUseRealInput = true;
        foreach (var flow in Flows.Where(f => f.Settings.IsLock && Flow.Walk(f.Steps).Any(s => s is MousePathStep or MouseLookStep)))
        {
            flow.Settings.InputMode = InputMode.Direct;
            _library.Save(flow);
        }
        _settingsService.Save();
    }

    partial void OnSelectedFlowChanged(Flow? oldValue, Flow? newValue)
    {
        if (oldValue != null && Flows.Contains(oldValue))
        {
            Editor?.TrackChanges();
            TrySave(oldValue);
        }
        Editor = newValue == null ? null : EditorFor(newValue);
        if (Editor != null) Editor.ShowRunSettings = Settings.ShowRunSettings;
        ListSelection = newValue;
        Settings.LastFlowId = newValue?.Id.ToString();
        if (newValue != null) IsSettingsOpen = false;
    }

    // Um editor por fluxo: mantém o histórico de desfazer e a seleção ao trocar de fluxo.
    private FlowEditorViewModel EditorFor(Flow flow)
    {
        if (_editors.TryGetValue(flow, out var existing)) return existing;
        var editor = new FlowEditorViewModel(flow, this);
        editor.PropertyChanged += (_, e) =>
        {
            if (!ReferenceEquals(Editor, editor)) return;
            if (e.PropertyName == nameof(FlowEditorViewModel.ShowRunSettings)) Settings.ShowRunSettings = editor.ShowRunSettings;
            if (e.PropertyName == nameof(FlowEditorViewModel.IsEmpty))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(StartMenuText));
            }
        };
        _editors[flow] = editor;
        return editor;
    }

    partial void OnListSelectionChanged(Flow? value)
    {
        if (value != null && !ReferenceEquals(value, SelectedFlow)) SelectedFlow = value;
    }

    partial void OnSearchTextChanged(string value) => SyncFilter();

    // Atualiza a lista filtrada sem recriar itens (a seleção não se perde).
    private void SyncFilter()
    {
        var q = SearchText.Trim();
        var wanted = Flows.Where(f => q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        for (var i = FilteredFlows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(FilteredFlows[i])) FilteredFlows.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var cur = FilteredFlows.IndexOf(wanted[i]);
            if (cur == i) continue;
            if (cur >= 0) FilteredFlows.Move(cur, i);
            else FilteredFlows.Insert(i, wanted[i]);
        }
        if (SelectedFlow != null && FilteredFlows.Contains(SelectedFlow)) ListSelection = SelectedFlow;
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand] private void Undo() => Editor?.UndoCommand.Execute(null);
    [RelayCommand] private void Redo() => Editor?.RedoCommand.Execute(null);
    [RelayCommand] private void CopyStep() => Editor?.CopyCommand.Execute(null);
    [RelayCommand] private void PasteStep() => Editor?.PasteCommand.Execute(null);
    [RelayCommand] private void DuplicateStep() => Editor?.DuplicateCommand.Execute(null);
    [RelayCommand] private void DeleteStep() => Editor?.DeleteCommand.Execute(null);

    [RelayCommand]
    private Task ShowHelp() => ConfirmAsync("Como usar",
        "1. Escolha um fluxo na lateral (ou Arquivo → Novo fluxo).\n" +
        "2. “Adicionar passo” monta o fluxo; clique num passo para ajustar.\n" +
        "3. Gravar (F9): use o PC e os passos são criados sozinhos, com o caminho exato do mouse.\n" +
        "4. Iniciar (F6) roda. Parar (F8): 1 vez termina a volta, 2 vezes para na hora.\n\n" +
        "Modo Lock (padrão): o fluxo clica e digita sem usar o seu mouse e teclado, e você continua usando o PC. " +
        "Funciona com a maioria dos programas, mas jogos costumam ignorar. Para jogos, desligue em Executar → Modo Lock: " +
        "o fluxo passa a usar o mouse de verdade, e mexer no mouse ou no teclado pausa na hora.",
        "Entendi", cancelText: null);

    [RelayCommand]
    private Task ShowShortcuts()
    {
        var h = Settings.Hotkeys;
        return ConfirmAsync("Atalhos do teclado",
            $"Em qualquer programa:\n{HotkeyList.Display(h.Start)}: iniciar\n{HotkeyList.Display(h.Pause)}: pausar / continuar\n{HotkeyList.Display(h.Stop)}: parar (2 vezes para na hora)\n{HotkeyList.Display(h.Record)}: gravar\n" +
            "Troque ou adicione teclas em Opções → Configurações.\n\n" +
            "No Macro Midas:\nCtrl+N novo fluxo · Ctrl+O abrir\nCtrl+Z / Ctrl+Y desfazer / refazer\nCtrl+C / Ctrl+V copiar / colar passo\nCtrl+D duplicar · Del excluir · Espaço ativar/desativar",
            "Ok", cancelText: null);
    }

    [RelayCommand]
    private Task ShowAbout() => ConfirmAsync("Macro Midas",
        $"Versão {SettingsPage.Version}\nAutomação de mouse e teclado para Windows.\nSeus fluxos ficam em {AppPaths.Root}",
        "Ok", cancelText: null);

    [RelayCommand]
    private void ToggleLog() => IsLogOpen = !IsLogOpen;

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private string UniqueName(string name)
    {
        if (Flows.All(f => f.Name != name)) return name;
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (Flows.All(f => f.Name != candidate)) return candidate;
        }
    }

    private long _lastNewFlow = long.MinValue / 2;

    [RelayCommand]
    private void NewFlow()
    {
        var now = Environment.TickCount64;
        if (now - _lastNewFlow < 600) return;
        _lastNewFlow = now;
        var flow = FlowTemplates.Empty();
        flow.Name = UniqueName(flow.Name);
        AddFlow(flow);
    }

    [RelayCommand]
    private void NewAutoClick()
    {
        var flow = FlowTemplates.AutoClick();
        flow.Name = UniqueName(flow.Name);
        AddFlow(flow);
    }

    private void AddFlow(Flow flow, int? index = null)
    {
        if (index is { } i && i >= 0 && i <= Flows.Count) Flows.Insert(i, flow);
        else Flows.Add(flow);
        _library.Save(flow);
        SyncFilter();
        SelectedFlow = flow;
        IsSettingsOpen = false;
        RegisterHotkeys();
    }

    [RelayCommand]
    private void DuplicateFlow(Flow? flow)
    {
        flow ??= SelectedFlow;
        if (flow == null) return;
        Editor?.TrackChanges();
        var copy = MmfSerializer.Clone(flow);
        copy.Id = Guid.NewGuid();
        copy.Name = UniqueName(flow.Name + " (cópia)");
        copy.CreatedAt = copy.ModifiedAt = DateTime.Now;
        copy.Settings.Hotkey = "";
        AddFlow(copy, Flows.IndexOf(flow) + 1);
    }

    [RelayCommand]
    private async Task DeleteFlow(Flow? flow)
    {
        flow ??= SelectedFlow;
        if (flow == null) return;
        if (Runner.IsBusy && Runner.FlowId == flow.Id)
        {
            Toast("Pare a execução antes de apagar este fluxo.", ToastKind.Warning);
            return;
        }
        if (Settings.ConfirmDelete && !await ConfirmAsync("Apagar fluxo",
                $"“{flow.Name}” será apagado permanentemente. Exporte antes se quiser guardar uma cópia.", "Apagar", danger: true))
            return;

        var idx = Flows.IndexOf(flow);
        Flows.Remove(flow);
        _editors.Remove(flow);
        _library.Delete(flow);
        SyncFilter();
        if (ReferenceEquals(SelectedFlow, flow))
            SelectedFlow = Flows.Count > 0 ? Flows[Math.Clamp(idx, 0, Flows.Count - 1)] : null;
        RegisterHotkeys();
        Toast($"“{flow.Name}” apagado.");
    }

    [RelayCommand]
    private async Task ClearSteps()
    {
        if (Editor == null || Editor.IsEmpty) return;
        if (!await ConfirmAsync("Limpar fluxo", "Remover todos os passos deste fluxo? (Ctrl+Z desfaz)", "Limpar", danger: true)) return;
        Editor.ClearAll();
    }

    [RelayCommand]
    private async Task Import()
    {
        if (Host?.Storage is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar fluxo",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Fluxos (*.mmf, *.txt)") { Patterns = ["*.mmf", "*.txt"] },
                new FilePickerFileType("Macro Midas Fluxe (*.mmf)") { Patterns = ["*.mmf"] },
                new FilePickerFileType("Texto (*.txt)") { Patterns = ["*.txt"] },
            ]
        });

        Flow? last = null;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path == null) continue;
            try
            {
                var flow = FlowLibrary.Import(path);
                flow.Name = UniqueName(flow.Name);
                AddFlow(flow);
                last = flow;
                if (FlowLibrary.ResolutionWarning(flow) is { } w) Toast(w, ToastKind.Warning);
            }
            catch (Exception ex)
            {
                await ConfirmAsync($"Não foi possível importar “{Path.GetFileName(path)}”", ex.Message, "OK", cancelText: null);
            }
        }
        if (last != null) Toast(files.Count == 1 ? $"“{last.Name}” importado" : "Fluxos importados", ToastKind.Success);
    }

    [RelayCommand]
    private async Task Export(string? format)
    {
        var flow = SelectedFlow;
        if (flow == null || Host?.Storage is not { } storage) return;
        Editor?.TrackChanges();
        var isTxt = format == "txt";
        var safe = string.Concat(flow.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = isTxt ? "Exportar como texto" : "Exportar fluxo (.mmf)",
            SuggestedFileName = safe + (isTxt ? ".txt" : ".mmf"),
            DefaultExtension = isTxt ? "txt" : "mmf",
            ShowOverwritePrompt = true,
            FileTypeChoices = isTxt
                ? [new FilePickerFileType("Texto (*.txt)") { Patterns = ["*.txt"] }]
                : [new FilePickerFileType("Macro Midas Fluxe (*.mmf)") { Patterns = ["*.mmf"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;
        try
        {
            FlowLibrary.Export(flow, path);
            Toast($"Exportado: {Path.GetFileName(path)}", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Toast("Falha ao exportar: " + ex.Message, ToastKind.Error);
        }
    }

    private void Autosave()
    {
        Editor?.TrackChanges();
        if (SelectedFlow != null && TrySave(SelectedFlow)) SaveState = $"Salvo às {DateTime.Now:HH:mm}";
        RegisterHotkeys();
    }

    private bool TrySave(Flow flow)
    {
        try
        {
            return _library.SaveIfChanged(flow);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Save", ex);
            SaveState = "Falha ao salvar!";
            return false;
        }
    }

    private void OnKeyCaptureStateChanged(bool capturing)
    {
        _capturingKey = capturing;
        if (_hotkeys == null) return;
        _hotkeys.Suspended = capturing;
        if (capturing)
        {
            _hotkeySignature = "";
            _ = _hotkeys.SetBindingsAsync([]);
        }
        else
        {
            Dispatcher.UIThread.Post(() => RegisterHotkeys(force: true), DispatcherPriority.Background);
        }
    }

    private async void RegisterHotkeys(bool force = false)
    {
        if (_hotkeys == null || _capturingKey) return;
        var h = Settings.Hotkeys;
        var list = new List<HotkeyService.Binding>();
        void Add(string name, string? keys, Action action)
        {
            foreach (var chord in HotkeyList.Split(keys)) list.Add(new HotkeyService.Binding(name, chord, action));
        }
        Add("Iniciar", h.Start, () => OnHotkeyAction(HotkeyAction.Start));
        Add("Pausar", h.Pause, () => OnHotkeyAction(HotkeyAction.Pause));
        Add("Parar", h.Stop, () => OnHotkeyAction(HotkeyAction.Stop));
        Add("Gravar", h.Record, () => _ = ToggleRecordAsync());
        foreach (var f in Flows.Where(f => !string.IsNullOrWhiteSpace(f.Settings.Hotkey)))
        {
            var flow = f;
            list.Add(new HotkeyService.Binding($"fluxo “{f.Name}”", f.Settings.Hotkey, () => StartFlow(flow)));
        }

        var signature = string.Join("|", list.Select(b => b.Name + "=" + VirtualKeysNormalize(b.Chord)));
        if (!force && signature == _hotkeySignature) return;
        _hotkeySignature = signature;

        var failed = await _hotkeys.SetBindingsAsync(list);
        SettingsPage.HotkeyWarning = failed.Count == 0 ? null : "Não registrados: " + string.Join(", ", failed);
        if (failed.Count > 0) Toast("Alguns atalhos não puderam ser registrados: " + string.Join(", ", failed), ToastKind.Warning);
    }

    private static string VirtualKeysNormalize(string chord) => Input.VirtualKeys.Normalize(chord) ?? chord;

    private IEnumerable<ushort> HotkeyVks()
    {
        var h = Settings.Hotkeys;
        foreach (var chord in new[] { h.Start, h.Pause, h.Stop, h.Record }.SelectMany(HotkeyList.Split))
            if (Input.VirtualKeys.TryParseChord(chord, out var c, out _))
                yield return c.Vk;
    }

    public void Toast(string message, ToastKind kind = ToastKind.Info)
    {
        var t = new ToastViewModel(message, kind);
        Toasts.Add(t);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);
        DispatcherTimer.RunOnce(() => Toasts.Remove(t),
            TimeSpan.FromSeconds(kind is ToastKind.Error or ToastKind.Warning ? 6 : 3.5));
    }

    [RelayCommand]
    private void DismissToast(ToastViewModel? t)
    {
        if (t != null) Toasts.Remove(t);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirmar", bool danger = false,
        string? cancelText = "Cancelar")
    {
        var d = new DialogViewModel
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText ?? "",
            IsDanger = danger
        };
        Dialog = d;
        var result = await d.Result;
        if (ReferenceEquals(Dialog, d)) Dialog = null;
        return result;
    }

    public void Shutdown()
    {
        _autosaveTimer.Stop();
        _uiTimer.Stop();
        if (Runner.IsBusy)
        {
            Runner.Kill();
            Runner.Join(TimeSpan.FromSeconds(2));
        }
        _recorder?.Dispose();
        StopGuard();
        Editor?.TrackChanges();
        foreach (var f in Flows) TrySave(f);
        SettingsPage.SaveNow();
        _hotkeys?.Dispose();
        Overlays?.CloseAll();
    }
}
