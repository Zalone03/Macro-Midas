namespace MacroMidas.Engine;

// Converte entre as coordenadas do fluxo e os pixels de verdade.
public readonly struct CoordinateSpace
{
    // Escala do modo proporcional: 0 a 1000 (o "1000×1000" da tela).
    public const int Scale = 1000;

    public static readonly CoordinateSpace Identity = default;

    private readonly bool _proportional;
    private readonly int _ox, _oy, _w, _h;

    private CoordinateSpace(int ox, int oy, int w, int h)
    {
        _proportional = true;
        _ox = ox;
        _oy = oy;
        _w = Math.Max(1, w);
        _h = Math.Max(1, h);
    }

    public bool IsProportional => _proportional;

    public static CoordinateSpace Proportional(int ox, int oy, int w, int h) => new(ox, oy, w, h);

    // Coordenada do fluxo → pixel.
    public (int X, int Y) ToDevice(int fx, int fy) => _proportional
        ? (_ox + (int)Math.Round(fx * (double)_w / Scale), _oy + (int)Math.Round(fy * (double)_h / Scale))
        : (fx, fy);

    // Pixel → coordenada do fluxo (usado ao capturar/gravar).
    public (int X, int Y) ToFlow(int dx, int dy) => _proportional
        ? ((int)Math.Round((dx - _ox) * (double)Scale / _w), (int)Math.Round((dy - _oy) * (double)Scale / _h))
        : (dx, dy);

    // Deslocamento do fluxo → deslocamento em pixels (arrasto/câmera, sem a origem).
    public (int X, int Y) DeltaToDevice(int fdx, int fdy) => _proportional
        ? ((int)Math.Round(fdx * (double)_w / Scale), (int)Math.Round(fdy * (double)_h / Scale))
        : (fdx, fdy);

    // Deslocamento em pixels → deslocamento do fluxo.
    public (int X, int Y) DeltaToFlow(int ddx, int ddy) => _proportional
        ? ((int)Math.Round(ddx * (double)Scale / _w), (int)Math.Round(ddy * (double)Scale / _h))
        : (ddx, ddy);
}
