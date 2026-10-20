using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroMidas.Engine;
using MacroMidas.Models;
using MacroMidas.Native;
using MacroMidas.Serialization;
using MacroMidas.Services;

namespace MacroMidas.ViewModels;

// O que o editor precisa do resto do app.
public interface IEditorHost
{
    AppSettings Settings { get; }
    PickerService? Picker { get; }
    IWindowHost? Host { get; }
    void Toast(string message, ToastKind kind = ToastKind.Info);
}

// Edição de um fluxo: inserir, mover, duplicar, copiar, desfazer e painel de propriedades.
public partial class FlowEditorViewModel : ObservableObject
{
    private const int UndoLimit = 100;
    private static string? _internalClipboard;

    private readonly IEditorHost _host;
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string _lastKnown;

    public Flow Flow { get; }
    public FlowRunSettings RunSettings => Flow.Settings;
    public IReadOnlyList<PaletteGroup> Palette => StepFactory.Groups;
    public ObservableCollection<WindowInfo> Windows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedIsConditional))]
    private FlowStep? _selectedStep;

    [ObservableProperty] private bool _showRunSettings = true;

    // "Mais opções" aberto no painel de propriedades (vale para todos os passos).
    [ObservableProperty] private bool _showAdvanced;

    public bool HasSelection => SelectedStep != null;
    public bool SelectedIsConditional => SelectedStep is ConditionalStepBase;
    public bool IsEmpty => Flow.Steps.Count == 0;

    // Resumo da execução em português simples (clicável no cabeçalho).
    public string RunSummary
    {
        get
        {
            var s = RunSettings;
            var parts = new List<string>
            {
                s.RepeatMode == RepeatMode.Infinite ? "Repete sem parar" : s.RepeatCount == 1 ? "Roda 1 vez" : $"Repete {s.RepeatCount} vezes"
            };
            if (s.Target == TargetKind.Window)
            {
                var name = string.IsNullOrWhiteSpace(s.WindowTitle) ? s.ProcessName : s.WindowTitle;
                parts.Add($"na janela “{name}”");
            }
            else parts.Add("na tela inteira");
            parts.Add(s.IsLock ? "Lock (seu mouse fica livre)" : "usa o seu mouse");
            if (!string.IsNullOrWhiteSpace(s.Hotkey)) parts.Add($"atalho {s.Hotkey}");
            return string.Join("  ·  ", parts);
        }
    }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // A view rola a lista até o passo indicado.
    public event Action<FlowStep>? BringIntoViewRequested;

    private CoordinateMode _lastCoordMode;
    private bool _restoring;

    public FlowEditorViewModel(Flow flow, IEditorHost host)
    {
        Flow = flow;
        _host = host;
        _lastCoordMode = flow.Settings.CoordinateMode;
        _lastKnown = Snapshot();
        Flow.Steps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Flow.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FlowRunSettings.CoordinateMode) && !_restoring) ConvertCoordinatesToMode();
            Flow.NotifyStructureChanged();
            OnPropertyChanged(nameof(RunSummary));
        };
        Renumber();
    }

    // Ao trocar "posições proporcionais", converte as posições existentes para o mesmo lugar.
    private void ConvertCoordinatesToMode()
    {
        var mode = Flow.Settings.CoordinateMode;
        if (mode == _lastCoordMode) return;
        _lastCoordMode = mode;

        var window = RunSettings.Target == TargetKind.Window
            ? WindowFinder.Find(RunSettings.WindowTitle, RunSettings.ProcessName)?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;
        var space = RunEnvironmentFactory.ProportionalSpace(window);
        if (mode == CoordinateMode.Proportional)
            CoordinateMap.Convert(Flow.Steps, space.ToFlow, space.DeltaToFlow);
        else
            CoordinateMap.Convert(Flow.Steps, space.ToDevice, space.DeltaToDevice);
        Renumber();
        TrackChanges();
    }

    partial void OnSelectedStepChanged(FlowStep? oldValue, FlowStep? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    [RelayCommand]
    private void Add(string kind)
    {
        Insert([StepFactory.Create(kind, _host.Settings)]);
        AskWhere(kind);
    }

    public void AddInto(IList<FlowStep> list, string kind)
    {
        var step = StepFactory.Create(kind, _host.Settings);
        Change(() => list.Add(step));
        Select(step);
        AskWhere(kind);
    }

    // Passo "Rolar" novo: já abre a mira para você mostrar onde a rolagem acontece.
    private void AskWhere(string kind)
    {
        if (kind == "scroll" && _host.Picker != null)
            _ = PickFor("Point", "Clique em cima do que deve rolar (a página, a lista…)  ·  Esc deixa para depois");
    }

    public void InsertSteps(IReadOnlyList<FlowStep> steps) => Insert(steps);

    private void Insert(IReadOnlyList<FlowStep> steps)
    {
        if (steps.Count == 0) return;
        Change(() =>
        {
            if (SelectedStep is { } sel && FindParent(sel) is { } list)
            {
                var at = list.IndexOf(sel) + 1;
                foreach (var s in steps) list.Insert(at++, s);
            }
            else
            {
                foreach (var s in steps) Flow.Steps.Add(s);
            }
        });
        Select(steps[^1]);
    }

    [RelayCommand]
    private void Select(FlowStep? step)
    {
        SelectedStep = step;
        if (step != null) BringIntoViewRequested?.Invoke(step);
    }

    [RelayCommand]
    private void Delete(FlowStep? step)
    {
        step ??= SelectedStep;
        if (step == null || FindParent(step) is not { } list) return;
        var idx = list.IndexOf(step);
        Change(() => list.Remove(step));
        if (ReferenceEquals(SelectedStep, step) || SelectedStep == null)
            SelectedStep = list.Count > 0 ? list[Math.Min(idx, list.Count - 1)] : FindOwner(list);
    }

    [RelayCommand]
    private void Duplicate(FlowStep? step)
    {
        step ??= SelectedStep;
        if (step == null || FindParent(step) is not { } list) return;
        var copy = MmfSerializer.CloneStep(step);
        Change(() => list.Insert(list.IndexOf(step) + 1, copy));
        Select(copy);
    }

    [RelayCommand]
    private void ToggleEnabled(FlowStep? step)
    {
        step ??= SelectedStep;
        if (step == null) return;
        Change(() => step.Enabled = !step.Enabled);
    }

    [RelayCommand]
    private void MoveUp(FlowStep? step)
    {
        step ??= SelectedStep;
        if (step == null || FindParent(step) is not { } list) return;
        var idx = list.IndexOf(step);
        Change(() =>
        {
            if (idx > 0)
            {
                var prev = list[idx - 1];
                if (prev.IsBlock)
                {
                    var lists = prev.GetChildLists().ToList();
                    var target = lists.LastOrDefault(l => l.Count > 0) ?? lists[0];
                    list.RemoveAt(idx);
                    target.Add(step);
                }
                else list.Move(idx, idx - 1);
                return;
            }

            if (FindOwner(list) is not { } owner) return;
            if (owner is IfStep f && ReferenceEquals(list, f.Else))
            {
                list.RemoveAt(idx);
                f.Then.Add(step);
                return;
            }
            var outer = FindParent(owner)!;
            list.RemoveAt(idx);
            outer.Insert(outer.IndexOf(owner), step);
        });
        Select(step);
    }

    [RelayCommand]
    private void MoveDown(FlowStep? step)
    {
        step ??= SelectedStep;
        if (step == null || FindParent(step) is not { } list) return;
        var idx = list.IndexOf(step);
        Change(() =>
        {
            if (idx < list.Count - 1)
            {
                var next = list[idx + 1];
                if (next.IsBlock)
                {
                    list.RemoveAt(idx);
                    next.GetChildLists().First().Insert(0, step);
                }
                else list.Move(idx, idx + 1);
                return;
            }

            if (FindOwner(list) is not { } owner) return;
            if (owner is IfStep f && ReferenceEquals(list, f.Then))
            {
                list.RemoveAt(idx);
                f.Else.Insert(0, step);
                return;
            }
            var outer = FindParent(owner)!;
            list.RemoveAt(idx);
            outer.Insert(outer.IndexOf(owner) + 1, step);
        });
        Select(step);
    }

    // Arrastar e soltar: move o passo para a posição index da lista alvo.
    public void MoveStepTo(FlowStep step, IList<FlowStep> target, int index)
    {
        if (FindParent(step) is not { } source) return;
        if (Flow.Walk([step]).Any(s => s.GetChildLists().Any(l => ReferenceEquals(l, target)))) return;

        if (ReferenceEquals(source, target))
        {
            var old = source.IndexOf(step);
            if (index > old) index--;
            index = Math.Clamp(index, 0, source.Count - 1);
            if (index == old) return;
            Change(() => source.Move(old, index));
        }
        else
        {
            Change(() =>
            {
                source.Remove(step);
                target.Insert(Math.Clamp(index, 0, target.Count), step);
            });
        }
        Select(step);
    }

    [RelayCommand]
    private async Task Copy(FlowStep? step)
    {
        step ??= SelectedStep;
        if (step == null) return;
        _internalClipboard = MmfSerializer.SerializeSteps([step]);
        if (_host.Host?.Clipboard is { } cb) await cb.SetTextAsync(TxtFlowWriter.WriteSteps([step]));
        _host.Toast("Passo copiado (também como texto)", ToastKind.Success);
    }

    [RelayCommand]
    private async Task Paste()
    {
        List<FlowStep> steps = [];
        var text = _host.Host?.Clipboard is { } cb ? await cb.TryGetTextAsync() : null;
        if (!string.IsNullOrWhiteSpace(text) && _internalClipboard != null &&
            text.Trim() == TxtFlowWriter.WriteSteps(MmfSerializer.DeserializeSteps(_internalClipboard)).Trim())
        {
            steps = MmfSerializer.DeserializeSteps(_internalClipboard);
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                steps = [.. TxtFlowParser.Parse(text).Steps];
            }
            catch (Exception ex)
            {
                _host.Toast("A área de transferência não tem passos válidos: " + ex.Message.Split('\n').Last(), ToastKind.Warning);
                return;
            }
        }
        else if (_internalClipboard != null)
        {
            steps = MmfSerializer.DeserializeSteps(_internalClipboard);
        }

        if (steps.Count == 0) return;
        Insert(steps);
        _host.Toast(steps.Count == 1 ? "Passo colado" : $"{steps.Count} passos colados", ToastKind.Success);
    }

    public void ClearAll()
    {
        Change(() => Flow.Steps.Clear());
        SelectedStep = null;
    }

    private string Snapshot() => MmfSerializer.Serialize(Flow);

    // Registra edições feitas no painel de propriedades como um passo de desfazer.
    public void TrackChanges()
    {
        var now = Snapshot();
        if (now == _lastKnown) return;
        _undo.Add(_lastKnown);
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        _redo.Clear();
        _lastKnown = now;
        NotifyUndo();
    }

    private void Change(Action action)
    {
        TrackChanges();
        action();
        Renumber();
        TrackChanges();
    }

    // Acrescenta uma trilha ao bloco "Ao mesmo tempo".
    [RelayCommand]
    private void AddTrack(ParallelStep step)
    {
        Change(step.AddTrack);
        Select(step);
    }

    // Remove uma trilha do bloco "Ao mesmo tempo" (sempre sobram pelo menos duas).
    [RelayCommand]
    private void RemoveTrack(ParallelTrack track)
    {
        var owner = Flow.Walk(Flow.Steps).OfType<ParallelStep>().FirstOrDefault(p => p.Tracks.Contains(track));
        if (owner is null || !owner.CanRemoveTracks) return;
        Change(() => owner.RemoveTrack(track));
        Select(owner);
    }

    [RelayCommand]
    private void Undo()
    {
        TrackChanges();
        if (_undo.Count == 0) return;
        _redo.Add(_lastKnown);
        var prev = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(prev);
    }

    [RelayCommand]
    private void Redo()
    {
        TrackChanges();
        if (_redo.Count == 0) return;
        _undo.Add(_lastKnown);
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(next);
    }

    private void Restore(string json)
    {
        var f = MmfSerializer.Deserialize(json);
        SelectedStep = null;
        Flow.Name = f.Name;
        Flow.Description = f.Description;
        _restoring = true;
        Flow.Settings.CopyFrom(f.Settings);
        _restoring = false;
        _lastCoordMode = Flow.Settings.CoordinateMode;
        Flow.Steps.Clear();
        foreach (var s in f.Steps) Flow.Steps.Add(s);
        Renumber();
        _lastKnown = Snapshot();
        NotifyUndo();
    }

    private void NotifyUndo()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Renumber()
    {
        Number(Flow.Steps);
        Flow.NotifyStructureChanged();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RunSummary));
    }

    private static void Number(IList<FlowStep> list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            list[i].DisplayIndex = (i + 1).ToString();
            foreach (var child in list[i].GetChildLists()) Number(child);
        }
    }

    public ObservableCollection<FlowStep>? FindParent(FlowStep step) => FindParent(step, Flow.Steps);

    private static ObservableCollection<FlowStep>? FindParent(FlowStep step, ObservableCollection<FlowStep> list)
    {
        if (list.Contains(step)) return list;
        foreach (var s in list)
        foreach (var child in s.GetChildLists())
            if (FindParent(step, child) is { } found)
                return found;
        return null;
    }

    private FlowStep? FindOwner(ObservableCollection<FlowStep> list) =>
        Flow.Walk(Flow.Steps).FirstOrDefault(s => s.GetChildLists().Any(l => ReferenceEquals(l, list)));
}
