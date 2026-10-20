using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacroMidas.Models;

// Passo base de um fluxo.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(MoveStep), "move")]
[JsonDerivedType(typeof(MousePathStep), "path")]
[JsonDerivedType(typeof(MouseLookStep), "look")]
[JsonDerivedType(typeof(ClickStep), "click")]
[JsonDerivedType(typeof(MouseButtonStep), "button")]
[JsonDerivedType(typeof(DragStep), "drag")]
[JsonDerivedType(typeof(ScrollStep), "scroll")]
[JsonDerivedType(typeof(KeyStep), "key")]
[JsonDerivedType(typeof(TypeTextStep), "type")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(IfStep), "if")]
[JsonDerivedType(typeof(WaitUntilStep), "waitUntil")]
[JsonDerivedType(typeof(RepeatStep), "repeat")]
[JsonDerivedType(typeof(HoldStep), "hold")]
[JsonDerivedType(typeof(ParallelStep), "parallel")]
[JsonDerivedType(typeof(StopStep), "stop")]
public abstract partial class FlowStep : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string? _note;

    // Estado só de interface (não vai para o arquivo).
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isSelected;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isRunning;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _displayIndex = "";

    [JsonIgnore] public abstract string Title { get; }

    // Frase simples que descreve o passo, ex.: "Clicar com o botão esquerdo em 960, 540".
    [JsonIgnore] public abstract string Summary { get; }

    // Explicação curta do que esse tipo de passo faz.
    [JsonIgnore] public abstract string Help { get; }

    [JsonIgnore] public abstract string Icon { get; }

    // Cor mostrada como bolinha ao lado da frase (passos que conferem cor); null nos demais.
    [JsonIgnore] public virtual string? SwatchColor => null;
    [JsonIgnore] public abstract StepCategory Category { get; }
    [JsonIgnore] public virtual bool IsBlock => false;
    [JsonIgnore] public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    // Listas de passos filhos (blocos Se / Repetir).
    public virtual IEnumerable<ObservableCollection<FlowStep>> GetChildLists() => [];

    private static readonly HashSet<string> UiOnly =
    [
        nameof(Summary), nameof(Title), nameof(IsSelected), nameof(IsRunning), nameof(DisplayIndex), nameof(HasNote), nameof(SwatchColor)
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is null || UiOnly.Contains(e.PropertyName)) return;
        RaiseSummaryChanged();
        if (e.PropertyName == nameof(Note)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasNote)));
    }

    protected void RaiseSummaryChanged()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Title)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(SwatchColor)));
    }

    protected static string Signed(int v) => v >= 0 ? $"+{v}" : v.ToString();

    protected static string Ms(int ms) => ms >= 1000 && ms % 100 == 0 ? $"{ms / 1000.0:0.#} s" : $"{ms} ms";

    // 500 → "0,5 segundo", 2000 → "2 segundos".
    public static string Sec(int ms)
    {
        var s = ms / 1000.0;
        return s < 2 ? $"{s:0.##} segundo" : $"{s:0.##} segundos";
    }
}
