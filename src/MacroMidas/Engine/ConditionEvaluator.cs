using MacroMidas.Capture;
using MacroMidas.Models;

namespace MacroMidas.Engine;

// Responde às perguntas dos passos "Se" / "Esperar até".
public static class ConditionEvaluator
{
    public static bool Evaluate(Condition c, IPixelReader pixels, IWindowQuery windows, out string detail, CoordinateSpace space = default)
    {
        bool raw;
        switch (c.Kind)
        {
            case ConditionKind.PixelColor:
            {
                var target = ParseColor(c.Color);
                var (x, y) = space.ToDevice(c.X, c.Y);
                var px = pixels.GetPixel(x, y);
                raw = px is { } p && p.Matches(target, c.Tolerance);
                detail = $"pixel ({c.X}, {c.Y}) = {px?.ToHex() ?? "indisponível"} vs {target.ToHex()} ±{c.Tolerance}";
                break;
            }
            case ConditionKind.ColorInRegion:
            {
                var target = ParseColor(c.Color);
                var (x, y) = space.ToDevice(c.X, c.Y);
                var (w, h) = space.DeltaToDevice(c.Width, c.Height);
                raw = pixels.FindColor(x, y, w, h, target, c.Tolerance, out var fx, out var fy);
                detail = raw
                    ? $"cor {target.ToHex()} achada em ({fx}, {fy})"
                    : $"cor {target.ToHex()} ±{c.Tolerance} não está em ({c.X}, {c.Y}, {c.Width}×{c.Height})";
                break;
            }
            case ConditionKind.WindowExists:
                raw = windows.Exists(c.WindowTitle);
                detail = $"janela “{c.WindowTitle}” {(raw ? "existe" : "não existe")}";
                break;
            case ConditionKind.WindowActive:
                raw = windows.IsActive(c.WindowTitle);
                detail = $"janela “{c.WindowTitle}” {(raw ? "em foco" : "fora de foco")}";
                break;
            default:
                raw = false;
                detail = c.Kind.ToString();
                break;
        }
        if (c.Negate) detail = "NÃO (" + detail + ")";
        return c.Negate ? !raw : raw;
    }

    private static RgbColor ParseColor(string hex) =>
        RgbColor.TryParse(hex, out var c) ? c : throw new InvalidOperationException($"Cor inválida na condição: “{hex}”");
}
