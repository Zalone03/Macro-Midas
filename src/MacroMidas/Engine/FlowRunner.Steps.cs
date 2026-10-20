using System.Diagnostics;
using System.Globalization;
using MacroMidas.Capture;
using MacroMidas.Input;
using MacroMidas.Models;

namespace MacroMidas.Engine;

public sealed partial class FlowRunner
{
    private enum ExecResult { Continue, StopFlow, NextIteration, Break }

    private bool _warnedLook;

    // Folga mínima entre eventos e tempo mínimo apertado, porque jogos perdem entrada muito rápida.
    private const int InputGapMs = 8;
    private const int MinHoldMs = 20;

    private ExecResult ExecList(IList<FlowStep> steps)
    {
        foreach (var step in steps)
        {
            if (!step.Enabled) continue;
            Checkpoint();
            SetCurrent(step);
            var r = Exec(step);
            if (r != ExecResult.Continue) return r;
        }
        return ExecResult.Continue;
    }

    private ExecResult Exec(FlowStep step)
    {
        var input = _env.Input;
        var space = _env.Space;
        switch (step)
        {
            case MoveStep m:
            {
                var (cx, cy) = input.GetPosition();
                int tx, ty;
                if (m.Relative)
                {
                    var (ddx, ddy) = space.DeltaToDevice(m.X, m.Y);
                    (tx, ty) = (cx + ddx, cy + ddy);
                }
                else (tx, ty) = space.ToDevice(m.X, m.Y);
                MoveSmooth(cx, cy, tx, ty, Scaled(m.DurationMs));
                break;
            }
            case ClickStep c:
            {
                if (c.UsePosition)
                {
                    var (px, py) = space.ToDevice(c.X, c.Y);
                    input.MoveTo(px, py);
                }
                var n = Math.Max(1, c.Clicks);
                for (var i = 0; i < n; i++)
                {
                    input.ButtonDown(c.Button, doubleClick: i % 2 == 1);
                    Sleep(Math.Max(InputGapMs, c.HoldMs));
                    input.ButtonUp(c.Button);
                    if (i < n - 1) Sleep(Math.Max(InputGapMs, c.IntervalMs));
                }
                break;
            }
            case MousePathStep p:
                FollowPath(p.Path);
                break;
            case MouseLookStep l:
                if (input is Input.MessageBackendBase)
                {
                    if (!_warnedLook) Log(LogLevel.Warning, "“Girar a câmera” não funciona no modo Lock (desligue em Executar → Modo Lock).");
                    _warnedLook = true;
                    break;
                }
                for (var i = 0; i + 2 < l.Path.Count; i += 3)
                {
                    if (l.Path[i] > 0) Sleep(Scaled(l.Path[i]));
                    else Checkpoint();
                    var (dx, dy) = space.DeltaToDevice(l.Path[i + 1], l.Path[i + 2]);
                    input.MoveBy(dx, dy);
                }
                break;
            case MouseButtonStep b:
                if (b.UsePosition)
                {
                    var (px, py) = space.ToDevice(b.X, b.Y);
                    input.MoveTo(px, py);
                }
                if (b.Action == ButtonAction.Press) input.ButtonDown(b.Button);
                else input.ButtonUp(b.Button);
                break;
            case DragStep d when d.HasPath:
            {
                var (fx, fy) = space.ToDevice(d.FromX, d.FromY);
                var (tx, ty) = space.ToDevice(d.ToX, d.ToY);
                input.MoveTo(fx, fy);
                Sleep(15);
                input.ButtonDown(d.Button);
                FollowPath(d.Path, d.RelativeMotion);
                var rest = d.DurationMs - PathMath.Duration(d.Path);
                if (rest > 0) Sleep(Scaled(rest));
                if (!d.RelativeMotion) input.MoveTo(tx, ty);
                input.ButtonUp(d.Button);
                break;
            }
            case DragStep d:
            {
                var (fx, fy) = space.ToDevice(d.FromX, d.FromY);
                var (tx, ty) = space.ToDevice(d.ToX, d.ToY);
                input.MoveTo(fx, fy);
                Sleep(15);
                input.ButtonDown(d.Button);
                Sleep(Math.Max(0, d.HoldMs));
                MoveSmooth(fx, fy, tx, ty, Math.Max(_opt.MoveStepMs * 3, Scaled(d.DurationMs)), d.RelativeMotion);
                Sleep(30);
                input.ButtonUp(d.Button);
                break;
            }
            case ScrollStep sc:
                if (sc.UsePosition)
                {
                    var (px, py) = space.ToDevice(sc.X, sc.Y);
                    input.MoveTo(px, py);
                    Sleep(15);
                }
                if (sc.ClickFirst)
                {
                    input.ButtonDown(MouseButtonKind.Left);
                    Sleep(30);
                    input.ButtonUp(MouseButtonKind.Left);
                    Sleep(80);
                }
                for (var i = 0; i < Math.Max(1, sc.Amount); i++)
                {
                    if (i > 0) Sleep(Math.Max(1, Scaled(sc.IntervalMs)));
                    input.Wheel(sc.Direction);
                }
                break;
            case KeyStep k:
                ExecKey(k);
                break;
            case TypeTextStep t:
                ExecType(t);
                break;
            case WaitStep w:
            {
                var lo = Math.Max(0, Math.Min(w.Ms, w.MaxMs));
                var hi = Math.Max(w.Ms, w.MaxMs);
                var ms = w.Random ? Random.Shared.Next(lo, hi + 1) : Math.Max(0, w.Ms);
                Sleep(Scaled(ms));
                break;
            }
            case IfStep f:
            {
                var ok = Evaluate(f.Condition, out var detail);
                if (_opt.LogConditions) Log(LogLevel.Info, $"Se {detail} → {(ok ? "SIM" : "NÃO")}");
                return ExecList(ok ? f.Then : f.Else);
            }
            case WaitUntilStep wu:
                return ExecWaitUntil(wu);
            case RepeatStep rp:
                return ExecRepeat(rp);
            case HoldStep h:
                return ExecHold(h);
            case ParallelStep pl:
                return ExecParallel(pl);
            case StopStep st:
                return st.Mode switch
                {
                    StopMode.NextIteration => ExecResult.NextIteration,
                    StopMode.BreakLoop => ExecResult.Break,
                    _ => ExecResult.StopFlow
                };
        }
        return ExecResult.Continue;
    }

    private ExecResult ExecHold(HoldStep h)
    {
        var input = _env.Input;
        ushort[] mods = [];
        ushort vk = 0;
        if (h.Kind == HoldInputKind.Key)
        {
            if (!VirtualKeys.TryParseChord(h.Keys, out var chord, out var err))
                throw new InvalidOperationException($"Tecla inválida “{h.Keys}”: {err}");
            mods = chord.ModifierVks().ToArray();
            vk = chord.Vk;
            foreach (var m in mods) { input.KeyDown(m); Sleep(InputGapMs); }
            input.KeyDown(vk);
            Sleep(InputGapMs);
        }
        else { input.ButtonDown(h.Button); Sleep(InputGapMs); }

        try
        {
            return ExecList(h.Body);
        }
        finally
        {
            if (h.Kind == HoldInputKind.Key)
            {
                input.KeyUp(vk);
                for (var i = mods.Length - 1; i >= 0; i--) input.KeyUp(mods[i]);
            }
            else input.ButtonUp(h.Button);
        }
    }

    // Roda as trilhas ao mesmo tempo, cada uma na sua thread.
    private ExecResult ExecParallel(ParallelStep p)
    {
        var tracks = p.Tracks.Select(t => t.Steps).Where(s => s.Any(st => st.Enabled)).ToList();
        if (tracks.Count == 0) return ExecResult.Continue;
        if (tracks.Count == 1) return ExecList(tracks[0]);

        var results = new ExecResult[tracks.Count];
        var threads = new Thread[tracks.Count];
        for (var i = 0; i < tracks.Count; i++)
        {
            var idx = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    results[idx] = ExecList(tracks[idx]);
                }
                catch (OperationCanceledException)
                {
                    results[idx] = ExecResult.StopFlow;
                }
                catch (Exception ex)
                {
                    results[idx] = ExecResult.StopFlow;
                    Log(LogLevel.Error, ex.Message);
                }
                if (results[idx] != ExecResult.Continue) _stopRequested = true;
            })
            {
                IsBackground = true,
                Name = $"MacroMidas.Parallel.{idx}",
                Priority = ThreadPriority.AboveNormal
            };
            threads[i].Start();
        }
        foreach (var th in threads) th.Join();

        if (results.Contains(ExecResult.StopFlow)) return ExecResult.StopFlow;
        if (results.Contains(ExecResult.Break)) return ExecResult.Break;
        if (results.Contains(ExecResult.NextIteration)) return ExecResult.NextIteration;
        return ExecResult.Continue;
    }

    private ExecResult ExecRepeat(RepeatStep rp)
    {
        if (!rp.Body.Any(s => s.Enabled)) return ExecResult.Continue;
        var infinite = rp.Count <= 0;
        for (var i = 0; infinite || i < rp.Count; i++)
        {
            var r = ExecList(rp.Body);
            if (r == ExecResult.Break) break;
            if (r != ExecResult.Continue) return r;
            if (infinite && _stopRequested) return ExecResult.StopFlow;
        }
        return ExecResult.Continue;
    }

    private ExecResult ExecWaitUntil(WaitUntilStep wu)
    {
        var start = _clock.ElapsedTicks;
        var pausedAtStart = _pausedTicks;
        var poll = Math.Max(5, wu.PollMs);
        while (true)
        {
            if (Evaluate(wu.Condition, out var detail))
            {
                if (_opt.LogConditions) Log(LogLevel.Info, $"Esperar até: {detail} ✓");
                return ExecResult.Continue;
            }

            var waitedMs = (_clock.ElapsedTicks - start - (_pausedTicks - pausedAtStart)) * 1000 / Stopwatch.Frequency;
            if (wu.TimeoutMs > 0 && waitedMs >= wu.TimeoutMs)
            {
                Log(LogLevel.Warning, $"Tempo esgotado ({wu.TimeoutMs} ms) esperando: {detail}");
                return wu.OnTimeout switch
                {
                    TimeoutAction.StopFlow => ExecResult.StopFlow,
                    TimeoutAction.NextIteration => ExecResult.NextIteration,
                    _ => ExecResult.Continue
                };
            }
            if (_stopRequested && wu.TimeoutMs <= 0) return ExecResult.StopFlow;
            Sleep(poll);
        }
    }

    private bool Evaluate(Condition c, out string detail) =>
        ConditionEvaluator.Evaluate(c, _env.Pixels, _env.Windows, out detail, _env.Space);

    private void ExecKey(KeyStep k)
    {
        if (!VirtualKeys.TryParseChord(k.Keys, out var chord, out var err))
            throw new InvalidOperationException($"Tecla inválida “{k.Keys}”: {err}");

        var input = _env.Input;
        var mods = chord.ModifierVks().ToArray();
        switch (k.Action)
        {
            case KeyAction.Down:
                foreach (var m in mods) input.KeyDown(m);
                input.KeyDown(chord.Vk);
                break;
            case KeyAction.Up:
                input.KeyUp(chord.Vk);
                for (var i = mods.Length - 1; i >= 0; i--) input.KeyUp(mods[i]);
                break;
            default:
                var times = Math.Max(1, k.Times);
                for (var t = 0; t < times; t++)
                {
                    foreach (var m in mods) { input.KeyDown(m); Sleep(InputGapMs); }
                    input.KeyDown(chord.Vk);
                    Sleep(Math.Max(MinHoldMs, k.HoldMs));
                    input.KeyUp(chord.Vk);
                    for (var i = mods.Length - 1; i >= 0; i--) { Sleep(InputGapMs); input.KeyUp(mods[i]); }
                    if (t < times - 1) Sleep(Math.Max(InputGapMs, Scaled(k.IntervalMs)));
                }
                break;
        }
    }

    private void ExecType(TypeTextStep t)
    {
        var text = (t.Text ?? "").Replace("\r\n", "\n");
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext())
        {
            Checkpoint();
            _env.Input.TypeChar(e.GetTextElement());
            if (t.CharDelayMs > 0) Sleep(Scaled(t.CharDelayMs));
        }
    }

    // Refaz um caminho gravado ([espera, x, y, ...]) respeitando os tempos.
    private void FollowPath(IReadOnlyList<int> path, bool relative = false)
    {
        var input = _env.Input;
        var space = _env.Space;
        int? lastX = null, lastY = null;
        for (var i = 0; i + 2 < path.Count; i += 3)
        {
            if (path[i] > 0) Sleep(Scaled(path[i]));
            else Checkpoint();
            var (x, y) = space.ToDevice(path[i + 1], path[i + 2]);
            if (relative)
            {
                if (lastX is { } lx && lastY is { } ly) input.MoveBy(x - lx, y - ly);
                (lastX, lastY) = (x, y);
            }
            else input.MoveTo(x, y);
        }
    }

    private void MoveSmooth(int x0, int y0, int x1, int y1, int durationMs, bool relative = false)
    {
        var input = _env.Input;
        int lastX = x0, lastY = y0;
        void Go(int x, int y)
        {
            if (relative) input.MoveBy(x - lastX, y - lastY);
            else input.MoveTo(x, y);
            lastX = x;
            lastY = y;
        }

        if (durationMs <= 0 || (x0 == x1 && y0 == y1))
        {
            Go(x1, y1);
            return;
        }

        var stepMs = Math.Max(1, _opt.MoveStepMs);
        var durTicks = durationMs * Stopwatch.Frequency / 1000;
        var start = _clock.ElapsedTicks;
        var pausedAtStart = _pausedTicks;
        while (true)
        {
            Checkpoint();
            var elapsed = _clock.ElapsedTicks - start - (_pausedTicks - pausedAtStart);
            var t = (double)elapsed / durTicks;
            if (t >= 1) break;
            var e = t * t * (3 - 2 * t);
            Go((int)Math.Round(x0 + (x1 - x0) * e), (int)Math.Round(y0 + (y1 - y0) * e));
            Sleep(stepMs);
        }
        Go(x1, y1);
    }

    private int Scaled(int ms) => ms <= 0 ? 0 : (int)Math.Round(ms / _speed);

    // Espera precisa que respeita pausa (o tempo pausado não conta) e kill.
    private void Sleep(int ms)
    {
        Checkpoint();
        if (ms <= 0) return;
        var target = _clock.ElapsedTicks + ms * Stopwatch.Frequency / 1000;
        var token = _kill.Token;
        while (true)
        {
            target += Checkpoint();
            var left = target - _clock.ElapsedTicks;
            if (left <= 0) return;
            var leftMs = left * 1000.0 / Stopwatch.Frequency;
            if (leftMs > 1.5)
            {
                if (token.WaitHandle.WaitOne((int)Math.Min(leftMs - 1, 50))) throw new OperationCanceledException(token);
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }

    // Bloqueia enquanto pausado e lança se o fluxo foi morto.
    private long Checkpoint()
    {
        var token = _kill.Token;
        token.ThrowIfCancellationRequested();
        if (_resume.IsSet) return 0;

        var t0 = _clock.ElapsedTicks;
        _env.Input.SuspendHeld();
        WaitHandle.WaitAny([_resume.WaitHandle, token.WaitHandle]);
        token.ThrowIfCancellationRequested();
        _env.Input.RestoreHeld();
        var paused = _clock.ElapsedTicks - t0;
        System.Threading.Interlocked.Add(ref _pausedTicks, paused);
        return paused;
    }
}
