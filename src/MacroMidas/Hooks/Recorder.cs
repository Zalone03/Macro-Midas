using System.Diagnostics;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Native;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Hooks;

// Grava mouse e teclado do usuário e converte em passos, no mesmo ritmo.
public sealed class Recorder : IDisposable
{
    private readonly RecordingSettings _settings;
    private readonly IntPtr _target;
    private readonly HashSet<ushort> _ignoredVks;
    private readonly List<HookEvent> _events = [];
    private readonly HashSet<MouseButtonKind> _ownClicks = [];
    private readonly object _lock = new();
    private LowLevelInputHook? _hook;
    private RawMouseInput? _raw;
    private long _lastMoveT = long.MinValue / 2;
    private int _lastMoveX = int.MinValue, _lastMoveY = int.MinValue;

    public Recorder(RecordingSettings settings, IntPtr target, IEnumerable<ushort> ignoredVks)
    {
        _settings = settings;
        _target = target;
        _ignoredVks = [.. ignoredVks];
    }

    public int EventCount
    {
        get { lock (_lock) return _events.Count; }
    }

    public bool Start()
    {
        var clock = Stopwatch.StartNew();
        _hook = new LowLevelInputHook(mouse: true, keyboard: _settings.RecordKeyboard, clock);
        _hook.Event += OnEvent;
        if (!_hook.Start()) return false;

        if (_settings.RecordMouseMoves)
        {
            _raw = new RawMouseInput(clock);
            _raw.Moved += OnRawMove;
            if (!_raw.Start())
            {
                _raw.Dispose();
                _raw = null;
            }
        }
        return true;
    }

    private void OnRawMove(long t, int dx, int dy)
    {
        if (!IsCursorHidden()) return;
        lock (_lock) _events.Add(new HookEvent(HookEventKind.RelMove, t, dx, dy));
    }

    public List<FlowStep> Stop(bool trimTrailingMoves = false)
    {
        _hook?.Dispose();
        _hook = null;
        _raw?.Dispose();
        _raw = null;
        List<HookEvent> copy;
        lock (_lock) copy = [.. _events];
        if (trimTrailingMoves) TrimTrailingMoves(copy);
        return BuildSteps(copy, _settings, (int)GetDoubleClickTime());
    }

    public void Dispose()
    {
        _hook?.Dispose();
        _raw?.Dispose();
    }

    private void OnEvent(HookEvent e)
    {
        if (e.FromMacroMidas) return;

        switch (e.Kind)
        {
            case HookEventKind.KeyDown or HookEventKind.KeyUp:
                if (!_settings.RecordKeyboard || _ignoredVks.Contains(e.Vk)) return;
                break;

            case HookEventKind.MouseMove:
                if (!_settings.RecordMouseMoves) return;
                if (e.X == _lastMoveX && e.Y == _lastMoveY) return;
                if (IsCursorHidden()) return;
                if (_settings.MoveMinIntervalMs > 0 && e.TimeMs - _lastMoveT < _settings.MoveMinIntervalMs) return;
                if (_settings.MoveMinDistance > 1 &&
                    Math.Max(Math.Abs(e.X - _lastMoveX), Math.Abs(e.Y - _lastMoveY)) < _settings.MoveMinDistance) return;
                _lastMoveT = e.TimeMs;
                _lastMoveX = e.X;
                _lastMoveY = e.Y;
                e = ToFlowCoords(e);
                break;

            case HookEventKind.Wheel:
                if (!_settings.RecordScroll || IsOwnWindowAt(e.X, e.Y)) return;
                e = ToFlowCoords(e);
                break;

            case HookEventKind.MouseDown:
                if (IsOwnWindowAt(e.X, e.Y))
                {
                    _ownClicks.Add(e.Button);
                    return;
                }
                e = ToFlowCoords(e);
                break;

            case HookEventKind.MouseUp:
                if (_ownClicks.Remove(e.Button)) return;
                e = ToFlowCoords(e);
                break;
        }

        lock (_lock) _events.Add(e);
    }

    private static bool IsOwnWindowAt(int x, int y) => WindowFinder.IsOwnWindow(WindowFromPoint(new POINT(x, y)));

    private HookEvent ToFlowCoords(HookEvent e)
    {
        if (_target == IntPtr.Zero) return e;
        var p = new POINT(e.X, e.Y);
        ScreenToClient(_target, ref p);
        return e with { X = p.X, Y = p.Y };
    }

    internal static void TrimTrailingMoves(List<HookEvent> events)
    {
        var last = events.FindLastIndex(e => e.Kind is not (HookEventKind.MouseMove or HookEventKind.RelMove));
        if (last >= 0) events.RemoveRange(last + 1, events.Count - last - 1);
    }

    private sealed record Timed(long Start, long End, FlowStep Step);

    // Botão apertado: onde/quando apertou, os movimentos até soltar e o quanto se afastou.
    private sealed class Press(HookEvent down, int seen)
    {
        public HookEvent Down { get; } = down;
        public int Seen { get; } = seen;
        public List<HookEvent> Moves { get; } = [];
        public int MaxDistance { get; private set; }

        public void Add(HookEvent move)
        {
            Moves.Add(move);
            MaxDistance = Math.Max(MaxDistance, Math.Max(Math.Abs(move.X - Down.X), Math.Abs(move.Y - Down.Y)));
        }
    }

    private static ushort NormalizeVk(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => vk
    };

    // [espera, x, y, ...] a partir de movimentos; a primeira espera conta desde since.
    private static List<int> ToPath(IReadOnlyList<HookEvent> moves, long since)
    {
        var path = new List<int>(moves.Count * 3);
        var prev = since;
        foreach (var m in moves)
        {
            path.Add((int)Math.Max(0, m.TimeMs - prev));
            path.Add(m.X);
            path.Add(m.Y);
            prev = m.TimeMs;
        }
        return path;
    }

    public static List<FlowStep> BuildSteps(IReadOnlyList<HookEvent> events, RecordingSettings s, int doubleClickMs)
    {
        var timed = new List<Timed>();
        var presses = new Dictionary<MouseButtonKind, Press>();
        var loose = new List<HookEvent>();
        var look = new List<HookEvent>();
        var heldMods = new List<ushort>();
        var modDown = new Dictionary<ushort, long>();
        var modUsed = new HashSet<ushort>();
        var keyDowns = new Dictionary<ushort, (long T, KeyChord Chord, int Seen)>();
        var seen = 0;

        void FlushPath()
        {
            if (loose.Count > 0)
            {
                timed.Add(new Timed(loose[0].TimeMs, loose[^1].TimeMs, new MousePathStep { Path = ToPath(loose, loose[0].TimeMs) }));
                loose.Clear();
            }
            if (look.Count > 0)
            {
                timed.Add(new Timed(look[0].TimeMs, look[^1].TimeMs, new MouseLookStep { Path = ToPath(look, look[0].TimeMs) }));
                look.Clear();
            }
        }

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case HookEventKind.MouseMove:
                    if (look.Count > 0) FlushPath();
                    if (presses.Count == 0)
                    {
                        if (loose.Count == 0) seen++;
                        loose.Add(e);
                    }
                    else foreach (var p in presses.Values) p.Add(e);
                    break;

                case HookEventKind.RelMove:
                    if (loose.Count > 0) FlushPath();
                    if (look.Count == 0) seen++;
                    look.Add(e);
                    break;

                case HookEventKind.MouseDown:
                    FlushPath();
                    seen++;
                    presses[e.Button] = new Press(e, seen);
                    break;

                case HookEventKind.MouseUp:
                {
                    if (!presses.Remove(e.Button, out var press)) break;
                    FlushPath();
                    var during = seen != press.Seen;
                    seen++;
                    var d = press.Down;
                    var dist = Math.Max(press.MaxDistance, Math.Max(Math.Abs(e.X - d.X), Math.Abs(e.Y - d.Y)));
                    var held = e.TimeMs - d.TimeMs;
                    if (dist > s.DragThreshold)
                    {
                        var drag = new DragStep { Button = e.Button, FromX = d.X, FromY = d.Y, ToX = e.X, ToY = e.Y };
                        if (press.Moves.Count > 0)
                        {
                            drag.Path = ToPath(press.Moves, d.TimeMs);
                            drag.HoldMs = 0;
                            drag.DurationMs = (int)Math.Max(1, held);
                        }
                        else
                        {
                            var hold = (int)Math.Min(80, held / 4);
                            drag.HoldMs = hold;
                            drag.DurationMs = (int)Math.Max(60, held - hold);
                        }
                        timed.Add(new Timed(d.TimeMs, e.TimeMs, drag));
                    }
                    else if (held >= s.LongPressMs || during)
                    {
                        timed.Add(new Timed(d.TimeMs, d.TimeMs, new MouseButtonStep
                            { Button = e.Button, Action = ButtonAction.Press, UsePosition = true, X = d.X, Y = d.Y }));
                        timed.Add(new Timed(e.TimeMs, e.TimeMs, new MouseButtonStep
                            { Button = e.Button, Action = ButtonAction.Release }));
                    }
                    else
                    {
                        timed.Add(new Timed(d.TimeMs, e.TimeMs, new ClickStep
                        {
                            Button = e.Button, UsePosition = true, X = d.X, Y = d.Y,
                            HoldMs = (int)Math.Clamp(held, 10, 300)
                        }));
                    }
                    break;
                }

                case HookEventKind.Wheel:
                    FlushPath();
                    seen++;
                    timed.Add(new Timed(e.TimeMs, e.TimeMs, new ScrollStep
                        { Direction = e.Wheel, Amount = 1, UsePosition = true, X = e.X, Y = e.Y }));
                    break;

                case HookEventKind.KeyDown:
                {
                    var vk = NormalizeVk(e.Vk);
                    if (VirtualKeys.IsModifier(vk))
                    {
                        if (heldMods.Contains(vk)) break;
                        FlushPath();
                        heldMods.Add(vk);
                        modDown[vk] = e.TimeMs;
                        modUsed.Remove(vk);
                    }
                    else
                    {
                        if (keyDowns.ContainsKey(vk)) break;
                        FlushPath();
                        seen++;
                        var chord = new KeyChord(vk, heldMods.Contains(0x11), heldMods.Contains(0x10), heldMods.Contains(0x12), heldMods.Contains(0x5B));
                        keyDowns[vk] = (e.TimeMs, chord, seen);
                        foreach (var m in heldMods) modUsed.Add(m);
                    }
                    break;
                }

                case HookEventKind.KeyUp:
                {
                    var vk = NormalizeVk(e.Vk);
                    if (VirtualKeys.IsModifier(vk))
                    {
                        if (!heldMods.Remove(vk)) break;
                        if (!modUsed.Contains(vk) && modDown.TryGetValue(vk, out var mt))
                        {
                            FlushPath();
                            AddKey(timed, new KeyChord(vk), mt, e.TimeMs, s, split: false);
                        }
                    }
                    else if (keyDowns.Remove(vk, out var kd))
                    {
                        FlushPath();
                        AddKey(timed, kd.Chord, kd.T, e.TimeMs, s, split: seen != kd.Seen);
                        seen++;
                    }
                    break;
                }
            }
        }
        FlushPath();

        timed.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = Merge(timed, doubleClickMs);

        while (merged.Count > 0 && merged[^1].Step is KeyStep { Action: KeyAction.Press } k &&
               VirtualKeys.TryParseChord(k.Keys, out var c, out _) && VirtualKeys.IsModifier(c.Vk))
            merged.RemoveAt(merged.Count - 1);

        var result = new List<FlowStep>();
        long? clock = null;
        foreach (var t in merged)
        {
            if (clock is { } now && s.RecordWaits)
            {
                var gap = (long)Math.Round((t.Start - now) * s.WaitScale);
                if (gap >= Math.Max(1, s.MinWaitMs))
                {
                    result.Add(new WaitStep { Ms = (int)gap });
                    now += gap;
                }
                else if (gap > 0 && (t.Step switch { MousePathStep p => p.Path, MouseLookStep l => l.Path, _ => null }) is { Count: > 0 } early)
                {
                    early[0] += (int)gap;
                    now += gap;
                }
                clock = now;
            }
            result.Add(t.Step);
            clock = (clock ?? t.Start) + Math.Max(0, t.End - t.Start);
        }
        return result;
    }

    private static void AddKey(List<Timed> timed, KeyChord chord, long down, long up, RecordingSettings s, bool split)
    {
        var keys = VirtualKeys.Format(chord);
        var held = up - down;
        if (split || held >= s.LongPressMs)
        {
            timed.Add(new Timed(down, down, new KeyStep { Keys = keys, Action = KeyAction.Down }));
            timed.Add(new Timed(up, up, new KeyStep { Keys = keys, Action = KeyAction.Up }));
        }
        else
        {
            timed.Add(new Timed(down, up, new KeyStep { Keys = keys, HoldMs = (int)Math.Clamp(held, 10, 300) }));
        }
    }

    private static List<Timed> Merge(List<Timed> items, int doubleClickMs)
    {
        var result = new List<Timed>();
        foreach (var t in items)
        {
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.Step is ClickStep a && t.Step is ClickStep b && a.Clicks == 1 && a.Button == b.Button &&
                    Math.Abs(a.X - b.X) <= 4 && Math.Abs(a.Y - b.Y) <= 4 && t.Start - last.End <= doubleClickMs)
                {
                    a.Clicks = 2;
                    a.IntervalMs = (int)Math.Clamp(t.Start - last.End, 10, doubleClickMs);
                    result[^1] = last with { End = t.End };
                    continue;
                }
                if (last.Step is ScrollStep sa && t.Step is ScrollStep sb && sa.Direction == sb.Direction &&
                    Math.Abs(sa.X - sb.X) <= 8 && Math.Abs(sa.Y - sb.Y) <= 8 && t.Start - last.End <= 400)
                {
                    sa.Amount += sb.Amount;
                    if (sa.Amount == 2) sa.IntervalMs = (int)Math.Clamp(t.Start - last.End, 10, 400);
                    result[^1] = last with { End = t.End };
                    continue;
                }
            }
            result.Add(t);
        }
        return result;
    }
}
