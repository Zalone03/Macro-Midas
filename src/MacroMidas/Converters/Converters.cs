using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using MacroMidas.Capture;
using MacroMidas.Engine;
using MacroMidas.Input;
using MacroMidas.Models;

namespace MacroMidas.Converters;

// Valor ↔ bool para grupos de RadioButton (parâmetro = nome do enum ou número).
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null) return BindingOperations.DoNothing;
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var text = parameter.ToString()!;
        if (type.IsEnum) return Enum.Parse(type, text, ignoreCase: true);
        try
        {
            return System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
        }
        catch
        {
            return BindingOperations.DoNothing;
        }
    }
}

// bool ↔ RadioButton (ConverterParameter = "True" ou "False").
public sealed class BoolEqualsConverter : IValueConverter
{
    public static readonly BoolEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && bool.TryParse(parameter?.ToString(), out var p) && b == p;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && bool.TryParse(parameter?.ToString(), out var p) ? p : BindingOperations.DoNothing;
}

// Milissegundos (int) ↔ segundos (decimal) para caixas de número mais fáceis de entender.
public sealed class MsToSecondsConverter : IValueConverter
{
    public static readonly MsToSecondsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int ms ? ms / 1000m : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal s ? (int)Math.Round(s * 1000m) : BindingOperations.DoNothing;
}

// Interruptor do modo Lock: ligado = segundo plano (Lock), desligado = direto.
public sealed class LockModeConverter : IValueConverter
{
    public static readonly LockModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is InputMode.Background;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? InputMode.Background : InputMode.Direct;
}

// Marcar "Repetir sem parar": ligado = infinito, desligado = número de vezes.
public sealed class RepeatInfiniteConverter : IValueConverter
{
    public static readonly RepeatInfiniteConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is RepeatMode.Infinite;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? RepeatMode.Infinite : RepeatMode.Count;
}

// Atalho salvo ("Ctrl+F6") → gesto mostrado ao lado do item do menu.
public sealed class HotkeyGestureConverter : IValueConverter
{
    public static readonly HotkeyGestureConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = HotkeyList.First(value as string);
        if (text.Length == 0) return null;
        try
        {
            return KeyGesture.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Interruptor "posições proporcionais": ligado = proporcional (0 a 1000), desligado = pixels.
public sealed class ProportionalModeConverter : IValueConverter
{
    public static readonly ProportionalModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is CoordinateMode.Proportional;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? CoordinateMode.Proportional : CoordinateMode.Pixels;
}

// int &gt; 0 → true.
public sealed class PositiveConverter : IValueConverter
{
    public static readonly PositiveConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i && i > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Contagem 0 = infinito (para o Repetir).
public sealed class ZeroIsInfiniteConverter : IValueConverter
{
    public static readonly ZeroIsInfiniteConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int i && i <= 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0 : 3;
}

// Nome do ícone ("Click") → geometria do recurso "Icon.Click".
public sealed class IconKeyConverter : IValueConverter
{
    public static readonly IconKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) ?? (parameter as string);
        if (key == null || Application.Current == null) return null;
        return Application.Current.TryFindResource("Icon." + key, out var g) ? g as Geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class CategoryBrushConverter : IValueConverter
{
    public static readonly CategoryBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            StepCategory.Keyboard => "CatKeyboardBrush",
            StepCategory.Time => "CatTimeBrush",
            StepCategory.Logic => "CatLogicBrush",
            _ => "CatMouseBrush"
        };
        return Application.Current?.TryFindResource(key, out var b) == true ? b : Brushes.Gold;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// "#RRGGBB" → pincel (para amostras de cor).
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        RgbColor.TryParse(value as string, out var c)
            ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LogLevelBrushConverter : IValueConverter
{
    public static readonly LogLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            LogLevel.Success => "SuccessBrush",
            LogLevel.Warning => "GoldBrightBrush",
            LogLevel.Error => "DangerBrush",
            _ => "Text2Brush"
        };
        return Application.Current?.TryFindResource(key, out var b) == true ? b : Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// 0.55 → "55%".
public sealed class PercentConverter : IValueConverter
{
    public static readonly PercentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? $"{Math.Round(d * 100)}%" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Velocidade 1.5 → "1,5×".
public sealed class SpeedConverter : IValueConverter
{
    public static readonly SpeedConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? $"{d:0.##}×" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Segundos com uma casa: 1.6 → "1,6 s".
public sealed class SecondsConverter : IValueConverter
{
    public static readonly SecondsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? $"{d:0.0} s" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
