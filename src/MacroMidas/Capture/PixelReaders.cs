using static MacroMidas.Native.Win32;

namespace MacroMidas.Capture;

// Leitura de pixels em "coordenadas do fluxo" (tela ou área cliente da janela alvo).
public interface IPixelReader
{
    string Describe { get; }

    RgbColor? GetPixel(int x, int y);

    bool FindColor(int x, int y, int width, int height, RgbColor target, int tolerance, out int foundX, out int foundY);
}

// Lê da tela composta.
public sealed class ScreenPixelReader(IntPtr targetHwnd) : IPixelReader
{
    public string Describe => targetHwnd == IntPtr.Zero ? "tela" : "tela (relativo à janela)";

    private POINT ToScreen(int x, int y)
    {
        var p = new POINT(x, y);
        if (targetHwnd != IntPtr.Zero) ClientToScreen(targetHwnd, ref p);
        return p;
    }

    public RgbColor? GetPixel(int x, int y)
    {
        var p = ToScreen(x, y);
        return GdiCapture.ScreenPixel(p.X, p.Y);
    }

    public bool FindColor(int x, int y, int width, int height, RgbColor target, int tolerance, out int foundX, out int foundY)
    {
        var p = ToScreen(x, y);
        var img = GdiCapture.CaptureScreen(p.X, p.Y, Math.Max(1, width), Math.Max(1, height));
        return ColorSearch.Find(img, 0, 0, img?.Width ?? 0, img?.Height ?? 0, target, tolerance, x, y, out foundX, out foundY);
    }
}

// Lê direto do conteúdo da janela (PrintWindow): funciona mesmo com a janela atrás de outras.
public sealed class WindowPixelReader(IntPtr hwnd) : IPixelReader
{
    private BgraImage? _frame;
    private long _frameTicks;
    private const long FrameReuseMs = 15;

    public string Describe => "conteúdo da janela";

    private BgraImage? Frame()
    {
        var now = Environment.TickCount64;
        if (_frame != null && now - _frameTicks <= FrameReuseMs) return _frame;
        _frame = GdiCapture.CaptureWindowClient(hwnd);
        _frameTicks = now;
        return _frame;
    }

    public RgbColor? GetPixel(int x, int y)
    {
        var f = Frame();
        return f != null && f.Contains(x, y) ? f.GetPixel(x, y) : null;
    }

    public bool FindColor(int x, int y, int width, int height, RgbColor target, int tolerance, out int foundX, out int foundY)
    {
        var f = Frame();
        return ColorSearch.Find(f, x, y, width, height, target, tolerance, 0, 0, out foundX, out foundY);
    }
}

internal static class ColorSearch
{
    // Procura na imagem a partir de (x, y); o resultado soma (offsetX, offsetY).
    public static bool Find(BgraImage? img, int x, int y, int width, int height, RgbColor target, int tolerance,
        int offsetX, int offsetY, out int foundX, out int foundY)
    {
        foundX = foundY = -1;
        if (img == null) return false;
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(img.Width, x + width);
        var y1 = Math.Min(img.Height, y + height);
        var px = img.Pixels;
        for (var yy = y0; yy < y1; yy++)
        {
            var row = yy * img.Width * 4;
            for (var xx = x0; xx < x1; xx++)
            {
                var i = row + xx * 4;
                if (Math.Abs(px[i + 2] - target.R) <= tolerance &&
                    Math.Abs(px[i + 1] - target.G) <= tolerance &&
                    Math.Abs(px[i] - target.B) <= tolerance)
                {
                    foundX = xx + offsetX;
                    foundY = yy + offsetY;
                    return true;
                }
            }
        }
        return false;
    }
}
