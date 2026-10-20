using MacroMidas.Input;

namespace MacroMidas.Hooks;

public enum HotkeyAction { Start, Pause, Stop }

// Segurança durante a execução: atalhos sempre ativos, devolver controle e parada de emergência.
public sealed class SessionGuard
{
    private readonly Dictionary<string, HotkeyAction> _chords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ushort> _down = [];
    private readonly bool _watchUser;
    private readonly bool _corner;
    private readonly int _cornerX, _cornerY;
    private long _lastTakeOver = long.MinValue / 2;
    private long _lastCorner = long.MinValue / 2;
    private (long T, int X, int Y)? _moveAnchor;

    // Uma das teclas de Iniciar/Pausar/Parar foi apertada.
    public event Action<HotkeyAction>? Hotkey;

    // O usuário mexeu no mouse ou no teclado (só no modo Direto).
    public event Action? UserTookOver;

    // Mouse no canto superior esquerdo.
    public event Action? Corner;

    public SessionGuard(IEnumerable<(HotkeyAction Action, string Keys)> hotkeys, bool watchUser, bool corner, int cornerX = 0, int cornerY = 0)
    {
        foreach (var (action, keys) in hotkeys)
            foreach (var chord in HotkeyList.Split(keys))
                if (VirtualKeys.TryParseChord(chord, out var c, out _))
                    _chords.TryAdd(VirtualKeys.Format(c), action);
        _watchUser = watchUser;
        _corner = corner;
        _cornerX = cornerX;
        _cornerY = cornerY;
    }

    public void Process(HookEvent e)
    {
        if (e.Injected) return;

        switch (e.Kind)
        {
            case HookEventKind.KeyDown:
            {
                var vk = Normalize(e.Vk);
                if (!_down.Add(vk)) return;
                if (VirtualKeys.IsModifier(vk)) return;
                var chord = new KeyChord(vk, _down.Contains(0x11), _down.Contains(0x10), _down.Contains(0x12), _down.Contains(0x5B));
                if (_chords.TryGetValue(VirtualKeys.Format(chord), out var action))
                {
                    Hotkey?.Invoke(action);
                    return;
                }
                TookOver(e.TimeMs);
                break;
            }
            case HookEventKind.KeyUp:
                _down.Remove(Normalize(e.Vk));
                break;
            case HookEventKind.MouseMove:
                if (_corner && e.X <= _cornerX + 2 && e.Y <= _cornerY + 2)
                {
                    if (e.TimeMs - _lastCorner < 1000) return;
                    _lastCorner = e.TimeMs;
                    Corner?.Invoke();
                    return;
                }
                if (_moveAnchor is not { } a || e.TimeMs - a.T > 400) _moveAnchor = (e.TimeMs, e.X, e.Y);
                else if (Math.Max(Math.Abs(e.X - a.X), Math.Abs(e.Y - a.Y)) >= 4) TookOver(e.TimeMs);
                break;
            case HookEventKind.MouseDown or HookEventKind.Wheel:
                TookOver(e.TimeMs);
                break;
        }
    }

    private void TookOver(long t)
    {
        if (!_watchUser || t - _lastTakeOver < 700) return;
        _lastTakeOver = t;
        UserTookOver?.Invoke();
    }

    private static ushort Normalize(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => vk
    };
}
