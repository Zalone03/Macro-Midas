using System.Runtime.InteropServices;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Capture;

// Captura de pixels via GDI.
internal static class GdiCapture
{
    // Copia uma área da tela (coordenadas físicas de tela).
    public static BgraImage? CaptureScreen(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        try
        {
            return CaptureFrom(width, height, memDc => BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY | CAPTUREBLT));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // Renderiza a área cliente de uma janela mesmo coberta por outras (PrintWindow).
    public static BgraImage? CaptureWindowClient(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rc) || rc.Width <= 0 || rc.Height <= 0) return null;
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            return CaptureFrom(rc.Width, rc.Height, memDc => PrintWindow(hwnd, memDc, PW_CLIENTONLY | PW_RENDERFULLCONTENT), screenDc);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static BgraImage? CaptureFrom(int width, int height, Func<IntPtr, bool> draw, IntPtr compatibleWith = default)
    {
        var memDc = CreateCompatibleDC(compatibleWith);
        if (memDc == IntPtr.Zero) return null;
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            }
        };
        var hBmp = CreateDIBSection(memDc, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (hBmp == IntPtr.Zero)
        {
            DeleteDC(memDc);
            return null;
        }

        var old = SelectObject(memDc, hBmp);
        try
        {
            if (!draw(memDc)) return null;
            var buffer = new byte[width * height * 4];
            Marshal.Copy(bits, buffer, 0, buffer.Length);
            return new BgraImage(width, height, buffer);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
        }
    }

    public static RgbColor? ScreenPixel(int x, int y) => CaptureScreen(x, y, 1, 1)?.GetPixel(0, 0);
}
