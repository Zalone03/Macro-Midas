using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacroMidas.Models;

public partial class Flow : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "Novo fluxo";
    [ObservableProperty] private string? _description;
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private DateTime _modifiedAt = DateTime.Now;

    public FlowRunSettings Settings { get; set; } = new();

    public ObservableCollection<FlowStep> Steps { get; set; } = [];

    [JsonIgnore] public int StepCount => CountSteps(Steps);

    [JsonIgnore]
    public string ListSummary
    {
        get
        {
            var n = StepCount;
            var steps = n == 1 ? "1 passo" : $"{n} passos";
            var rep = Settings.RepeatMode == RepeatMode.Infinite ? "contínuo" :
                Settings.RepeatCount == 1 ? "1 vez" : $"{Settings.RepeatCount} vezes";
            var lockTxt = Settings.IsLock ? " · lock" : "";
            return $"{steps} · {rep}{lockTxt}";
        }
    }

    public void NotifyStructureChanged()
    {
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(ListSummary));
    }

    public static int CountSteps(IEnumerable<FlowStep> steps)
    {
        var total = 0;
        foreach (var s in steps)
        {
            total++;
            foreach (var list in s.GetChildLists()) total += CountSteps(list);
        }
        return total;
    }

    // Percorre todos os passos (pré-ordem), incluindo os aninhados.
    public static IEnumerable<FlowStep> Walk(IEnumerable<FlowStep> steps)
    {
        foreach (var s in steps)
        {
            yield return s;
            foreach (var list in s.GetChildLists())
            foreach (var child in Walk(list))
                yield return child;
        }
    }
}

public partial class FlowRunSettings : ObservableObject
{
    [ObservableProperty] private RepeatMode _repeatMode = RepeatMode.Count;
    [ObservableProperty] private int _repeatCount = 1;
    [ObservableProperty] private int _iterationDelayMs;
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private int _startDelayMs = 300;

    [ObservableProperty] private TargetKind _target = TargetKind.Screen;
    [ObservableProperty] private string _windowTitle = "";
    [ObservableProperty] private string _processName = "";

    // Lock (padrão): o fluxo não mexe no mouse/teclado do usuário.
    [ObservableProperty] private InputMode _inputMode = InputMode.Background;

    // Atalho opcional que inicia este fluxo diretamente (ex.: "Ctrl+F1").
    [ObservableProperty] private string _hotkey = "";

    // Como as posições são guardadas: pixels fixos ou proporção da tela (0 a 1000).
    [ObservableProperty] private CoordinateMode _coordinateMode = CoordinateMode.Pixels;

    // Resolução da tela principal em que o fluxo foi salvo (para avisar ao importar em outra).
    [ObservableProperty] private int _authorScreenWidth;
    [ObservableProperty] private int _authorScreenHeight;

    [JsonIgnore] public bool IsWindowTarget => Target == TargetKind.Window;
    [JsonIgnore] public bool IsLock => InputMode == InputMode.Background;
    [JsonIgnore] public bool IsProportional => CoordinateMode == CoordinateMode.Proportional;

    partial void OnTargetChanged(TargetKind value) => OnPropertyChanged(nameof(IsWindowTarget));

    partial void OnInputModeChanged(InputMode value) => OnPropertyChanged(nameof(IsLock));

    partial void OnCoordinateModeChanged(CoordinateMode value) => OnPropertyChanged(nameof(IsProportional));

    public void CopyFrom(FlowRunSettings o)
    {
        RepeatMode = o.RepeatMode;
        RepeatCount = o.RepeatCount;
        IterationDelayMs = o.IterationDelayMs;
        Speed = o.Speed;
        StartDelayMs = o.StartDelayMs;
        Target = o.Target;
        WindowTitle = o.WindowTitle;
        ProcessName = o.ProcessName;
        InputMode = o.InputMode;
        Hotkey = o.Hotkey;
        CoordinateMode = o.CoordinateMode;
        AuthorScreenWidth = o.AuthorScreenWidth;
        AuthorScreenHeight = o.AuthorScreenHeight;
    }
}
