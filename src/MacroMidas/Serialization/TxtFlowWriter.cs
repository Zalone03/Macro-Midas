using System.Globalization;
using System.Text;
using MacroMidas.Models;

namespace MacroMidas.Serialization;

// Exporta um fluxo como roteiro de texto legível (um passo por linha).
public static class TxtFlowWriter
{
    public static string Write(Flow flow)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Macro Midas: fluxo em texto");
        sb.AppendLine("# Um passo por linha. Blocos if/else/repeat terminam com 'end'. Linhas com # são comentários.");
        sb.AppendLine("# Prefixo 'off:' desativa um passo; '// texto' no fim da linha vira anotação.");
        sb.AppendLine("@macro-midas 1");
        sb.AppendLine($"@name {Q(flow.Name)}");
        if (!string.IsNullOrWhiteSpace(flow.Description)) sb.AppendLine($"@description {Q(flow.Description!)}");

        var s = flow.Settings;
        sb.AppendLine(s.RepeatMode == RepeatMode.Infinite ? "@repeat forever" : $"@repeat {s.RepeatCount}");
        sb.AppendLine($"@interval {s.IterationDelayMs}");
        sb.AppendLine($"@speed {s.Speed.ToString("0.###", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"@startdelay {s.StartDelayMs}");
        sb.AppendLine(s.Target == TargetKind.Window
            ? $"@target window title={Q(s.WindowTitle)} process={Q(s.ProcessName)}"
            : "@target screen");
        sb.AppendLine($"@input {(s.InputMode == InputMode.Background ? "background" : "direct")}");
        if (s.CoordinateMode == CoordinateMode.Proportional) sb.AppendLine("@coords proportional");
        if (s.AuthorScreenWidth > 0 && s.AuthorScreenHeight > 0) sb.AppendLine($"@screen {s.AuthorScreenWidth}x{s.AuthorScreenHeight}");
        if (!string.IsNullOrWhiteSpace(s.Hotkey)) sb.AppendLine($"@hotkey {Q(s.Hotkey)}");
        sb.AppendLine();
        WriteSteps(sb, flow.Steps, 0);
        return sb.ToString();
    }

    public static string WriteSteps(IEnumerable<FlowStep> steps)
    {
        var sb = new StringBuilder();
        WriteSteps(sb, steps, 0);
        return sb.ToString();
    }

    private static void WriteSteps(StringBuilder sb, IEnumerable<FlowStep> steps, int depth)
    {
        foreach (var step in steps) WriteStep(sb, step, depth);
    }

    private static void Line(StringBuilder sb, int depth, FlowStep step, string text)
    {
        sb.Append(' ', depth * 2);
        if (!step.Enabled) sb.Append("off: ");
        sb.Append(text);
        if (!string.IsNullOrWhiteSpace(step.Note)) sb.Append("  // ").Append(step.Note!.Replace("\r", " ").Replace("\n", " "));
        sb.AppendLine();
    }

    private static void Plain(StringBuilder sb, int depth, string text)
    {
        sb.Append(' ', depth * 2).AppendLine(text);
    }

    private static void WriteStep(StringBuilder sb, FlowStep step, int depth)
    {
        switch (step)
        {
            case MoveStep m:
                Line(sb, depth, m, $"{(m.Relative ? "moveby" : "move")} {m.X} {m.Y}{Opt("duration", m.DurationMs, 0)}");
                break;
            case MousePathStep p:
                Line(sb, depth, p, $"path{PathText(p.Path)}");
                break;
            case MouseLookStep l:
                Line(sb, depth, l, $"look{PathText(l.Path)}");
                break;
            case ClickStep c:
            {
                var d = new ClickStep();
                Line(sb, depth, c, $"click {Btn(c.Button)}{(c.UsePosition ? $" {c.X} {c.Y}" : "")}" +
                                   $"{Opt("count", c.Clicks, d.Clicks)}{Opt("hold", c.HoldMs, d.HoldMs)}{Opt("interval", c.IntervalMs, d.IntervalMs)}");
                break;
            }
            case MouseButtonStep b:
                Line(sb, depth, b, $"{(b.Action == ButtonAction.Press ? "down" : "up")} {Btn(b.Button)}{(b.UsePosition ? $" {b.X} {b.Y}" : "")}");
                break;
            case DragStep g:
            {
                var d = new DragStep();
                Line(sb, depth, g, $"drag {Btn(g.Button)} {g.FromX} {g.FromY} {g.ToX} {g.ToY}" +
                                   $"{Opt("duration", g.DurationMs, d.DurationMs)}{Opt("hold", g.HoldMs, d.HoldMs)}" +
                                   (g.RelativeMotion ? " rel=1" : "") +
                                   (g.HasPath ? " path" + PathText(g.Path) : ""));
                break;
            }
            case ScrollStep sc:
            {
                var d = new ScrollStep();
                Line(sb, depth, sc, $"scroll {sc.Direction.ToString().ToLowerInvariant()} {sc.Amount}" +
                                    $"{(sc.UsePosition ? $" at {sc.X} {sc.Y}" : "")}{Opt("interval", sc.IntervalMs, d.IntervalMs)}" +
                                    (sc.ClickFirst ? " click=1" : ""));
                break;
            }
            case KeyStep k:
            {
                var d = new KeyStep();
                var verb = k.Action switch { KeyAction.Down => "keydown", KeyAction.Up => "keyup", _ => "key" };
                var opts = k.Action == KeyAction.Press
                    ? $"{Opt("hold", k.HoldMs, d.HoldMs)}{Opt("times", k.Times, d.Times)}{Opt("interval", k.IntervalMs, d.IntervalMs)}"
                    : "";
                Line(sb, depth, k, $"{verb} {KeyToken(k.Keys)}{opts}");
                break;
            }
            case TypeTextStep t:
                Line(sb, depth, t, $"type {Q(t.Text)}{Opt("delay", t.CharDelayMs, new TypeTextStep().CharDelayMs)}");
                break;
            case WaitStep w:
                Line(sb, depth, w, w.Random ? $"wait {Math.Min(w.Ms, w.MaxMs)}-{Math.Max(w.Ms, w.MaxMs)}" : $"wait {w.Ms}");
                break;
            case IfStep f:
                Line(sb, depth, f, $"if {Cond(f.Condition)}");
                WriteSteps(sb, f.Then, depth + 1);
                if (f.Else.Count > 0)
                {
                    Plain(sb, depth, "else");
                    WriteSteps(sb, f.Else, depth + 1);
                }
                Plain(sb, depth, "end");
                break;
            case WaitUntilStep wu:
            {
                var d = new WaitUntilStep();
                var onTimeout = wu.OnTimeout switch
                {
                    TimeoutAction.StopFlow => "stop",
                    TimeoutAction.NextIteration => "next",
                    _ => "continue"
                };
                Line(sb, depth, wu, $"waituntil {Cond(wu.Condition)}{Opt("timeout", wu.TimeoutMs, d.TimeoutMs)}" +
                                    $"{Opt("poll", wu.PollMs, d.PollMs)}{(wu.OnTimeout != d.OnTimeout ? $" ontimeout={onTimeout}" : "")}");
                break;
            }
            case RepeatStep r:
                Line(sb, depth, r, r.Count <= 0 ? "repeat forever" : $"repeat {r.Count}");
                WriteSteps(sb, r.Body, depth + 1);
                Plain(sb, depth, "end");
                break;
            case HoldStep h:
                Line(sb, depth, h, h.Kind == HoldInputKind.Key ? $"hold key {KeyToken(h.Keys)}" : $"hold mouse {Btn(h.Button)}");
                WriteSteps(sb, h.Body, depth + 1);
                Plain(sb, depth, "end");
                break;
            case ParallelStep pl:
                Line(sb, depth, pl, "parallel");
                for (var i = 0; i < pl.Tracks.Count; i++)
                {
                    if (i > 0) Plain(sb, depth, "also");
                    WriteSteps(sb, pl.Tracks[i].Steps, depth + 1);
                }
                Plain(sb, depth, "end");
                break;
            case StopStep st:
                Line(sb, depth, st, st.Mode switch { StopMode.NextIteration => "next", StopMode.BreakLoop => "break", _ => "stop" });
                break;
        }
    }

    private static string Cond(Condition c)
    {
        var not = c.Negate ? "not " : "";
        return c.Kind switch
        {
            ConditionKind.PixelColor => $"{not}pixel {c.X} {c.Y} {c.Color}{Opt("tol", c.Tolerance, -1)}",
            ConditionKind.ColorInRegion => $"{not}region {c.X} {c.Y} {c.Width} {c.Height} {c.Color}{Opt("tol", c.Tolerance, -1)}",
            ConditionKind.WindowExists => $"{not}window {Q(c.WindowTitle)}",
            ConditionKind.WindowActive => $"{not}active {Q(c.WindowTitle)}",
            _ => "pixel 0 0 #000000"
        };
    }

    private static string Btn(MouseButtonKind b) => b.ToString().ToLowerInvariant();

    // " 0,100,200 8,102,201 …" (espera,x,y de cada ponto).
    private static string PathText(IReadOnlyList<int> path)
    {
        var sb = new StringBuilder();
        for (var i = 0; i + 2 < path.Count; i += 3) sb.Append(' ').Append(path[i]).Append(',').Append(path[i + 1]).Append(',').Append(path[i + 2]);
        return sb.ToString();
    }

    private static string Opt(string name, int value, int def) => value == def ? "" : $" {name}={value}";

    private static string KeyToken(string keys) =>
        string.IsNullOrWhiteSpace(keys) ? Q("") : keys.Any(char.IsWhiteSpace) ? Q(keys) : keys;

    // Texto entre aspas com escapes (\" \\ \n \t).
    public static string Q(string? s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s ?? "")
        {
            sb.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => ch.ToString()
            });
        }
        return sb.Append('"').ToString();
    }
}
