using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacroMidas.Models;

public partial class MoveStep : FlowStep
{
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private bool _relative;
    [ObservableProperty] private int _durationMs;

    public override string Title => "Mover o mouse";
    public override string Summary => Relative ? $"Mover o mouse {RelativeText(X, Y)}" : $"Mover o mouse até {X}, {Y}";
    public override string Help => "Leva o cursor até um ponto da tela, sem clicar.";
    public override string Icon => "Move";
    public override StepCategory Category => StepCategory.Mouse;

    private static string RelativeText(int dx, int dy)
    {
        var parts = new List<string>();
        if (dx != 0) parts.Add($"{Math.Abs(dx)} px para a {(dx > 0 ? "direita" : "esquerda")}");
        if (dy != 0) parts.Add($"{Math.Abs(dy)} px para {(dy > 0 ? "baixo" : "cima")}");
        return parts.Count == 0 ? "(sem deslocamento)" : string.Join(" e ", parts);
    }
}

// Caminho do mouse gravado, com o tempo exato entre cada ponto.
public partial class MousePathStep : FlowStep
{
    private List<int> _path = [];

    // Pontos em sequência: [espera ms, x, y, espera ms, x, y, ...].
    public List<int> Path
    {
        get => _path;
        set
        {
            _path = value ?? [];
            OnPropertyChanged();
        }
    }

    [JsonIgnore] public int PointCount => Path.Count / 3;
    [JsonIgnore] public int DurationMs => PathMath.Duration(Path);
    [JsonIgnore] public string StartText => PointCount == 0 ? "-" : $"{Path[1]}, {Path[2]}";
    [JsonIgnore] public string EndText => PointCount == 0 ? "-" : $"{Path[^2]}, {Path[^1]}";
    [JsonIgnore] public string DurationText => Sec(DurationMs);

    public override string Title => "Caminho do mouse";
    public override string Summary => PointCount == 0
        ? "Mover o mouse (caminho vazio)"
        : $"Mover o mouse pelo caminho gravado até {Path[^2]}, {Path[^1]} ({Sec(DurationMs)})";
    public override string Help => "Refaz exatamente o movimento do mouse que você gravou, no mesmo ritmo.";
    public override string Icon => "Move";
    public override StepCategory Category => StepCategory.Mouse;
}

// Movimento relativo gravado com o cursor preso (girar a câmera num jogo): [espera, dx, dy, ...].
public partial class MouseLookStep : FlowStep
{
    private List<int> _path = [];

    public List<int> Path
    {
        get => _path;
        set
        {
            _path = value ?? [];
            OnPropertyChanged();
        }
    }

    [JsonIgnore] public int PointCount => Path.Count / 3;
    [JsonIgnore] public int DurationMs => PathMath.Duration(Path);
    [JsonIgnore] public string DurationText => Sec(DurationMs);

    [JsonIgnore]
    public string TotalText
    {
        get
        {
            int dx = 0, dy = 0;
            for (var i = 0; i + 2 < Path.Count; i += 3)
            {
                dx += Path[i + 1];
                dy += Path[i + 2];
            }
            return $"{dx:+0;-0;0}, {dy:+0;-0;0}";
        }
    }

    public override string Title => "Girar a câmera";
    public override string Summary => $"Girar a câmera com o mouse ({Sec(DurationMs)})";
    public override string Help => "Gravado num jogo com o cursor escondido: refaz o movimento do mouse (girar a câmera). Precisa do modo Lock desligado.";
    public override string Icon => "Move";
    public override StepCategory Category => StepCategory.Mouse;
}

// Contas sobre caminhos no formato [espera, x, y, ...].
public static class PathMath
{
    public static int Duration(IReadOnlyList<int> path)
    {
        var t = 0;
        for (var i = 0; i + 2 < path.Count; i += 3) t += Math.Max(0, path[i]);
        return t;
    }
}

public partial class ClickStep : FlowStep
{
    [ObservableProperty] private MouseButtonKind _button = MouseButtonKind.Left;
    [ObservableProperty] private int _clicks = 1;
    [ObservableProperty] private bool _usePosition = true;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _holdMs = 30;
    [ObservableProperty] private int _intervalMs = 60;

    public override string Title => "Clicar";
    public override string Summary
    {
        get
        {
            var verb = Clicks switch { <= 1 => "Clicar", 2 => "Clicar duas vezes", _ => $"Clicar {Clicks} vezes" };
            var where = UsePosition ? $"em {X}, {Y}" : "onde o mouse estiver";
            return $"{verb} com o botão {Labels.Of(Button)} {where}";
        }
    }
    public override string Help => "Clica com o mouse num ponto da tela ou onde o cursor estiver.";
    public override string Icon => "Click";
    public override StepCategory Category => StepCategory.Mouse;
}

// Segurar ou soltar um botão do mouse (para cliques longos e arrastos manuais).
public partial class MouseButtonStep : FlowStep
{
    [ObservableProperty] private MouseButtonKind _button = MouseButtonKind.Left;
    [ObservableProperty] private ButtonAction _action = ButtonAction.Press;
    [ObservableProperty] private bool _usePosition;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;

    public override string Title => Action == ButtonAction.Press ? "Segurar botão" : "Soltar botão";
    public override string Summary => Action == ButtonAction.Press
        ? $"Segurar o botão {Labels.Of(Button)}" + (UsePosition ? $" em {X}, {Y}" : "")
        : $"Soltar o botão {Labels.Of(Button)}";
    public override string Help => "Mantém o botão do mouse apertado até um passo “Soltar”. Serve para arrastar em etapas.";
    public override string Icon => "Hold";
    public override StepCategory Category => StepCategory.Mouse;
}

public partial class DragStep : FlowStep
{
    [ObservableProperty] private MouseButtonKind _button = MouseButtonKind.Left;
    [ObservableProperty] private int _fromX;
    [ObservableProperty] private int _fromY;
    [ObservableProperty] private int _toX;
    [ObservableProperty] private int _toY;
    [ObservableProperty] private int _durationMs = 400;
    [ObservableProperty] private int _holdMs = 80;

    // Movimento em deslocamento (relativo): jogos que leem o mouse bruto reconhecem o arrasto.
    [ObservableProperty] private bool _relativeMotion;

    private List<int> _path = [];

    // Trajeto gravado com o botão apertado ([espera, x, y, ...]); vazio = linha reta.
    public List<int> Path
    {
        get => _path;
        set
        {
            _path = value ?? [];
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPath));
            OnPropertyChanged(nameof(PathInfo));
        }
    }

    [JsonIgnore] public bool HasPath => Path.Count >= 3;
    [JsonIgnore] public string PathInfo => HasPath ? $"Trajeto gravado: {Path.Count / 3} pontos em {Sec(PathMath.Duration(Path))}." : "";

    public override string Title => "Arrastar";
    public override string Summary => $"Arrastar de {FromX}, {FromY} até {ToX}, {ToY}" + (HasPath ? " pelo caminho gravado" : "");
    public override string Help => "Segura o botão, leva o mouse de um ponto até outro e solta.";
    public override string Icon => "Drag";
    public override StepCategory Category => StepCategory.Mouse;
}

public partial class ScrollStep : FlowStep
{
    [ObservableProperty] private ScrollDirection _direction = ScrollDirection.Down;
    [ObservableProperty] private int _amount = 3;
    [ObservableProperty] private bool _usePosition;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _intervalMs = 40;

    // Clica no ponto antes de rolar, para a janela certa receber a rolagem.
    [ObservableProperty] private bool _clickFirst;

    public override string Title => "Rolar";
    public override string Summary
    {
        get
        {
            var dir = Direction switch
            {
                ScrollDirection.Up => "para cima",
                ScrollDirection.Left => "para a esquerda",
                ScrollDirection.Right => "para a direita",
                _ => "para baixo"
            };
            var where = UsePosition ? $"{X}, {Y}" : "onde o mouse estiver";
            return ClickFirst
                ? (UsePosition ? $"Clicar em {where} e rolar {Amount}× {dir}" : $"Clicar e rolar {Amount}× {dir} onde o mouse estiver")
                : $"Rolar {Amount}× {dir} " + (UsePosition ? $"em {where}" : where);
        }
    }
    public override string Help => "Gira a rodinha do mouse num ponto da tela, como para descer uma página.";
    public override string Icon => "Scroll";
    public override StepCategory Category => StepCategory.Mouse;
}

public partial class KeyStep : FlowStep
{
    // Combinação no formato "Ctrl+Shift+S", "Enter", "F5"...
    [ObservableProperty] private string _keys = "Enter";
    [ObservableProperty] private KeyAction _action = KeyAction.Press;
    [ObservableProperty] private int _holdMs = 30;
    [ObservableProperty] private int _times = 1;
    [ObservableProperty] private int _intervalMs = 50;

    public override string Title => "Tecla";
    public override string Summary
    {
        get
        {
            var keys = string.IsNullOrWhiteSpace(Keys) ? "(nenhuma tecla)" : Keys;
            return Action switch
            {
                KeyAction.Down => $"Segurar {keys}",
                KeyAction.Up => $"Soltar {keys}",
                _ => Times > 1 ? $"Apertar {keys} {Times} vezes" : $"Apertar {keys}"
            };
        }
    }
    public override string Help => "Aperta uma tecla ou um atalho do teclado, como Enter ou Ctrl+C.";
    public override string Icon => "Key";
    public override StepCategory Category => StepCategory.Keyboard;
}

public partial class TypeTextStep : FlowStep
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private int _charDelayMs = 15;

    public override string Title => "Digitar";
    public override string Summary
    {
        get
        {
            var t = (Text ?? "").Replace("\r", "").Replace("\n", " ⏎ ");
            return t.Length == 0 ? "Digitar (texto vazio)" : $"Digitar “{(t.Length > 48 ? t[..48] + "…" : t)}”";
        }
    }
    public override string Help => "Escreve um texto como se você estivesse digitando.";
    public override string Icon => "Type";
    public override StepCategory Category => StepCategory.Keyboard;
}

public partial class WaitStep : FlowStep
{
    [ObservableProperty] private int _ms = 500;
    [ObservableProperty] private bool _random;
    [ObservableProperty] private int _maxMs = 1000;

    public override string Title => "Esperar";
    public override string Summary => Random
        ? $"Esperar de {Math.Min(Ms, MaxMs) / 1000.0:0.##} a {Sec(Math.Max(Ms, MaxMs))}"
        : $"Esperar {Sec(Ms)}";
    public override string Help => "Faz uma pausa antes do próximo passo.";
    public override string Icon => "Wait";
    public override StepCategory Category => StepCategory.Time;
}

// Base para passos que carregam uma condição (repassa as mudanças para o resumo).
public abstract partial class ConditionalStepBase : FlowStep
{
    private Condition _condition = new();

    public Condition Condition
    {
        get => _condition;
        set
        {
            if (ReferenceEquals(_condition, value)) return;
            _condition.PropertyChanged -= OnConditionChanged;
            _condition = value ?? new Condition();
            _condition.PropertyChanged += OnConditionChanged;
            OnPropertyChanged();
        }
    }

    protected ConditionalStepBase() => _condition.PropertyChanged += OnConditionChanged;

    public override string? SwatchColor => Condition.UsesColor ? Condition.Color : null;

    private void OnConditionChanged(object? sender, PropertyChangedEventArgs e) => RaiseSummaryChanged();
}

public partial class IfStep : ConditionalStepBase
{
    public ObservableCollection<FlowStep> Then { get; set; } = [];
    public ObservableCollection<FlowStep> Else { get; set; } = [];

    public override string Title => "Se…";
    public override string Summary => "Se " + Condition.IfClause;
    public override string Help => "Confere algo na tela e escolhe o caminho: faz os passos de “Se sim” ou os de “Se não”.";
    public override string Icon => "If";
    public override StepCategory Category => StepCategory.Logic;
    public override bool IsBlock => true;
    public override IEnumerable<ObservableCollection<FlowStep>> GetChildLists() => [Then, Else];
}

public partial class WaitUntilStep : ConditionalStepBase
{
    [ObservableProperty] private int _timeoutMs = 10000;
    [ObservableProperty] private int _pollMs = 50;
    [ObservableProperty] private TimeoutAction _onTimeout = TimeoutAction.Continue;

    public override string Title => "Esperar até…";
    public override string Summary =>
        "Esperar até que " + Condition.UntilClause + (TimeoutMs > 0 ? $" (no máximo {Sec(TimeoutMs)})" : "");
    public override string Help => "Fica esperando a tela mudar antes de continuar.";
    public override string Icon => "WaitUntil";
    public override StepCategory Category => StepCategory.Logic;
}

public partial class RepeatStep : FlowStep
{
    // Quantidade de repetições; 0 = infinito (até parar).
    [ObservableProperty] private int _count = 3;

    public ObservableCollection<FlowStep> Body { get; set; } = [];

    public override string Title => "Repetir";
    public override string Summary => Count <= 0 ? "Repetir sem parar" : Count == 1 ? "Repetir 1 vez" : $"Repetir {Count} vezes";
    public override string Help => "Faz os passos de dentro várias vezes seguidas.";
    public override string Icon => "Repeat";
    public override StepCategory Category => StepCategory.Logic;
    public override bool IsBlock => true;
    public override IEnumerable<ObservableCollection<FlowStep>> GetChildLists() => [Body];
}

// Mantém uma tecla ou botão apertado enquanto os passos de dentro rodam.
public partial class HoldStep : FlowStep
{
    [ObservableProperty] private HoldInputKind _kind = HoldInputKind.Key;
    [ObservableProperty] private string _keys = "W";
    [ObservableProperty] private MouseButtonKind _button = MouseButtonKind.Left;

    public ObservableCollection<FlowStep> Body { get; set; } = [];

    public override string Title => "Segurar enquanto";
    public override string Summary => Kind == HoldInputKind.Key
        ? $"Segurar {(string.IsNullOrWhiteSpace(Keys) ? "(tecla)" : Keys)} enquanto faz os passos abaixo"
        : $"Segurar o botão {Labels.Of(Button)} enquanto faz os passos abaixo";
    public override string Help =>
        "Mantém uma tecla ou um botão apertado enquanto os passos de dentro rodam, e solta no fim. " +
        "Serve para fazer coisas ao mesmo tempo (ex.: segurar W e clicar).";
    public override string Icon => "Hold";
    public override StepCategory Category => StepCategory.Logic;
    public override bool IsBlock => true;
    public override IEnumerable<ObservableCollection<FlowStep>> GetChildLists() => [Body];
}

// Roda várias trilhas lado a lado, todas ao mesmo tempo.
public partial class ParallelStep : FlowStep
{
    public ObservableCollection<ParallelTrack> Tracks { get; set; } = [new(), new()];

    [JsonIgnore] public int TrackCount => Tracks.Count;

    public override string Title => "Ao mesmo tempo";
    public override string Summary => $"Fazer {Tracks.Count} coisas ao mesmo tempo (lado a lado)";
    public override string Help =>
        "Roda as trilhas lado a lado, todas ao mesmo tempo. Ex.: uma trilha segura W (anda) e a outra " +
        "clica. O bloco termina quando todas as trilhas terminam.";
    public override string Icon => "Parallel";
    public override StepCategory Category => StepCategory.Logic;
    public override bool IsBlock => true;
    public override IEnumerable<ObservableCollection<FlowStep>> GetChildLists() => Tracks.Select(t => t.Steps);

    public void AddTrack()
    {
        Tracks.Add(new ParallelTrack());
        TracksChanged();
    }

    public void RemoveTrack(ParallelTrack track)
    {
        if (Tracks.Count > 2 && Tracks.Remove(track)) TracksChanged();
    }

    [JsonIgnore] public bool CanRemoveTracks => Tracks.Count > 2;

    private void TracksChanged()
    {
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(CanRemoveTracks));
        RaiseSummaryChanged();
    }
}

// Uma trilha do bloco "Ao mesmo tempo", que roda em paralelo com as outras.
public sealed class ParallelTrack
{
    public ObservableCollection<FlowStep> Steps { get; set; } = [];
}

public partial class StopStep : FlowStep
{
    [ObservableProperty] private StopMode _mode = StopMode.StopFlow;

    public override string Title => "Parar";
    public override string Summary => Mode switch
    {
        StopMode.NextIteration => "Recomeçar do início (próximo ciclo)",
        StopMode.BreakLoop => "Sair do “Repetir”",
        _ => "Parar o fluxo"
    };
    public override string Help => "Encerra o fluxo, recomeça do início ou sai de um “Repetir”.";
    public override string Icon => "StopFlow";
    public override StepCategory Category => StepCategory.Logic;
}
