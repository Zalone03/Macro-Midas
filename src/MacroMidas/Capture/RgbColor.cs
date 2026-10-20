using System.Globalization;

namespace MacroMidas.Capture;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    // Maior diferença entre os canais (0 = idêntica).
    public int DistanceTo(RgbColor o) => Math.Max(Math.Abs(R - o.R), Math.Max(Math.Abs(G - o.G), Math.Abs(B - o.B)));

    public bool Matches(RgbColor o, int tolerance) => DistanceTo(o) <= tolerance;

    public static bool TryParse(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length == 8) s = s[2..];
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static RgbColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Cor inválida: {text}");
}

// Imagem BGRA 32 bits de cima para baixo (formato do DIB do GDI).
public sealed class BgraImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    public RgbColor GetPixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return new RgbColor(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}
