using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MacroMidas.Models;

// Pergunta que o motor faz antes de decidir o próximo passo ("Caso Se").
public partial class Condition : ObservableObject
{
    [ObservableProperty] private ConditionKind _kind = ConditionKind.PixelColor;
    [ObservableProperty] private bool _negate;

    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _width = 40;
    [ObservableProperty] private int _height = 40;

    // Cor alvo em "#RRGGBB".
    [ObservableProperty] private string _color = "#FFD54A";

    // Diferença máxima permitida por canal (0 = cor exata).
    [ObservableProperty] private int _tolerance = 24;

    [ObservableProperty] private string _windowTitle = "";

    // Texto curto para o registro e para o resumo técnico.
    [JsonIgnore]
    public string Summary => IfClause;

    // Complemento de "Se …" (ex.: "o ponto 960, 540 estiver com a cor #FFD54A").
    [JsonIgnore]
    public string IfClause => Kind switch
    {
        ConditionKind.PixelColor => Negate
            ? $"o ponto {X}, {Y} não estiver com a cor escolhida"
            : $"o ponto {X}, {Y} estiver com a cor escolhida",
        ConditionKind.ColorInRegion => Negate
            ? $"a cor escolhida não aparecer na área {X}, {Y} ({Width}×{Height})"
            : $"a cor escolhida aparecer na área {X}, {Y} ({Width}×{Height})",
        ConditionKind.WindowExists => Negate
            ? $"a janela “{WindowTitle}” não estiver aberta"
            : $"a janela “{WindowTitle}” estiver aberta",
        ConditionKind.WindowActive => Negate
            ? $"a janela “{WindowTitle}” não estiver em primeiro plano"
            : $"a janela “{WindowTitle}” estiver em primeiro plano",
        _ => Kind.ToString()
    };

    // Complemento de "Esperar até que …" (ex.: "o ponto 960, 540 fique com a cor #FFD54A").
    [JsonIgnore]
    public string UntilClause => Kind switch
    {
        ConditionKind.PixelColor => Negate
            ? $"o ponto {X}, {Y} saia da cor escolhida"
            : $"o ponto {X}, {Y} fique com a cor escolhida",
        ConditionKind.ColorInRegion => Negate
            ? $"a cor escolhida suma da área {X}, {Y} ({Width}×{Height})"
            : $"a cor escolhida apareça na área {X}, {Y} ({Width}×{Height})",
        ConditionKind.WindowExists => Negate
            ? $"a janela “{WindowTitle}” feche"
            : $"a janela “{WindowTitle}” abra",
        ConditionKind.WindowActive => Negate
            ? $"a janela “{WindowTitle}” saia do primeiro plano"
            : $"a janela “{WindowTitle}” fique em primeiro plano",
        _ => Kind.ToString()
    };

    [JsonIgnore] public bool UsesPixel => Kind is ConditionKind.PixelColor;
    [JsonIgnore] public bool UsesRegion => Kind is ConditionKind.ColorInRegion;
    [JsonIgnore] public bool UsesColor => Kind is ConditionKind.PixelColor or ConditionKind.ColorInRegion;
    [JsonIgnore] public bool UsesWindow => Kind is ConditionKind.WindowExists or ConditionKind.WindowActive;

    private static readonly string[] Derived =
        [nameof(Summary), nameof(IfClause), nameof(UntilClause), nameof(UsesPixel), nameof(UsesRegion), nameof(UsesColor), nameof(UsesWindow)];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (Array.IndexOf(Derived, e.PropertyName) >= 0) return;
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IfClause)));
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(UntilClause)));
        if (e.PropertyName == nameof(Kind))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(UsesPixel)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(UsesRegion)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(UsesColor)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(UsesWindow)));
        }
    }
}
