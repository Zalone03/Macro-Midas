using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using MacroMidas.Models;

namespace MacroMidas.Serialization;

public sealed class FlowParseException(IReadOnlyList<string> errors)
    : Exception("O texto do fluxo tem erros:\n" + string.Join("\n", errors.Take(8)) + (errors.Count > 8 ? $"\n… e mais {errors.Count - 8}" : ""))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

// Lê o roteiro de texto gerado por TxtFlowWriter (e escrito à mão).
public static class TxtFlowParser
{
    private sealed record Token(string Text, bool Quoted)
    {
        public bool IsOption(out string key, out string value)
        {
            key = value = "";
            if (Quoted) return false;
            var i = Text.IndexOf('=');
            if (i <= 0) return false;
            key = Text[..i].ToLowerInvariant();
            value = Text[(i + 1)..];
            return true;
        }
    }

    private sealed class Frame(FlowStep block, ObservableCollection<FlowStep> list, int line)
    {
        public FlowStep Block { get; } = block;
        public ObservableCollection<FlowStep> List { get; set; } = list;
        public int Line { get; } = line;
        public bool InElse { get; set; }
        public int TrackIndex { get; set; }
    }

    public static Flow Parse(string text, string fallbackName = "Fluxo importado")
    {
        var flow = new Flow { Name = fallbackName };
        var errors = new List<string>();
        var stack = new Stack<Frame>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (var n = 0; n < lines.Length; n++)
        {
            var lineNo = n + 1;
            var raw = lines[n].Trim();
            if (raw.Length == 0 || raw.StartsWith('#')) continue;
            try
            {
                var (tokens, note) = Tokenize(raw);
                if (tokens.Count == 0) continue;

                var enabled = true;
                if (!tokens[0].Quoted && tokens[0].Text.Equals("off:", StringComparison.OrdinalIgnoreCase))
                {
                    enabled = false;
                    tokens.RemoveAt(0);
                    if (tokens.Count == 0) continue;
                }

                var verb = tokens[0].Text.ToLowerInvariant();
                var args = tokens.Skip(1).ToList();
                var current = stack.Count > 0 ? stack.Peek().List : flow.Steps;

                if (verb.StartsWith('@'))
                {
                    ParseHeader(flow, verb, args);
                    continue;
                }

                switch (verb)
                {
                    case "else":
                        if (stack.Count == 0 || stack.Peek().Block is not IfStep || stack.Peek().InElse)
                            throw new FormatException("'else' sem um 'if' aberto");
                        stack.Peek().InElse = true;
                        stack.Peek().List = ((IfStep)stack.Peek().Block).Else;
                        continue;
                    case "also":
                        if (stack.Count == 0 || stack.Peek().Block is not ParallelStep pll)
                            throw new FormatException("'also' sem um 'parallel' aberto");
                        var fr = stack.Peek();
                        fr.TrackIndex++;
                        while (pll.Tracks.Count <= fr.TrackIndex) pll.AddTrack();
                        fr.List = pll.Tracks[fr.TrackIndex].Steps;
                        continue;
                    case "end":
                        if (stack.Count == 0) throw new FormatException("'end' sem bloco aberto");
                        stack.Pop();
                        continue;
                }

                var step = ParseStep(verb, args);
                step.Enabled = enabled;
                if (!string.IsNullOrWhiteSpace(note)) step.Note = note;
                current.Add(step);

                switch (step)
                {
                    case IfStep f:
                        stack.Push(new Frame(f, f.Then, lineNo));
                        break;
                    case RepeatStep r:
                        stack.Push(new Frame(r, r.Body, lineNo));
                        break;
                    case HoldStep h:
                        stack.Push(new Frame(h, h.Body, lineNo));
                        break;
                    case ParallelStep pl:
                        stack.Push(new Frame(pl, pl.Tracks[0].Steps, lineNo));
                        break;
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                errors.Add($"Linha {lineNo}: {ex.Message}");
            }
        }

        foreach (var open in stack)
            errors.Add($"Linha {open.Line}: bloco “{open.Block.Title}” sem 'end'");

        if (errors.Count > 0) throw new FlowParseException(errors);
        return flow;
    }

    private static void ParseHeader(Flow flow, string verb, List<Token> args)
    {
        var s = flow.Settings;
        string First() => args.Count > 0 ? args[0].Text : throw new FormatException($"{verb} precisa de um valor");
        switch (verb)
        {
            case "@macro-midas":
                break;
            case "@name":
                flow.Name = First();
                break;
            case "@description":
                flow.Description = First();
                break;
            case "@repeat":
                var r = First().ToLowerInvariant();
                if (r is "forever" or "infinite" or "infinito" or "0")
                    s.RepeatMode = RepeatMode.Infinite;
                else
                {
                    s.RepeatMode = RepeatMode.Count;
                    s.RepeatCount = Math.Max(1, Int(r));
                }
                break;
            case "@interval":
                s.IterationDelayMs = Math.Max(0, Int(First()));
                break;
            case "@speed":
                s.Speed = double.Parse(First().Replace(',', '.'), CultureInfo.InvariantCulture);
                break;
            case "@startdelay":
                s.StartDelayMs = Math.Max(0, Int(First()));
                break;
            case "@target":
                var kind = First().ToLowerInvariant();
                if (kind == "screen")
                {
                    s.Target = TargetKind.Screen;
                }
                else if (kind == "window")
                {
                    s.Target = TargetKind.Window;
                    foreach (var a in args.Skip(1))
                    {
                        if (!a.IsOption(out var key, out var value)) continue;
                        if (key == "title") s.WindowTitle = value;
                        else if (key == "process") s.ProcessName = value;
                    }
                }
                else throw new FormatException($"@target inválido: {kind} (use screen ou window)");
                break;
            case "@input":
                s.InputMode = First().ToLowerInvariant() switch
                {
                    "background" or "lock" => InputMode.Background,
                    "direct" => InputMode.Direct,
                    var x => throw new FormatException($"@input inválido: {x} (use direct ou background)")
                };
                break;
            case "@hotkey":
                s.Hotkey = First();
                break;
            case "@coords":
                s.CoordinateMode = First().ToLowerInvariant() switch
                {
                    "proportional" or "proporcional" => CoordinateMode.Proportional,
                    "pixels" or "pixel" => CoordinateMode.Pixels,
                    var x => throw new FormatException($"@coords inválido: {x} (use pixels ou proportional)")
                };
                break;
            case "@screen":
            {
                var parts = First().ToLowerInvariant().Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out var sw) && int.TryParse(parts[1], out var sh))
                {
                    s.AuthorScreenWidth = sw;
                    s.AuthorScreenHeight = sh;
                }
                break;
            }
            default:
                throw new FormatException($"Cabeçalho desconhecido: {verb}");
        }
    }

    // "0,100,200 8,102,201 …" → [0,100,200,8,102,201,…].
    private static List<int> ParsePath(IEnumerable<Token> tokens)
    {
        var path = new List<int>();
        foreach (var t in tokens)
        {
            var parts = t.Text.Split(',');
            if (parts.Length != 3) throw new FormatException($"Ponto de caminho inválido: {t.Text} (use espera,x,y)");
            foreach (var p in parts) path.Add(Int(p));
        }
        return path;
    }

    private static FlowStep ParseStep(string verb, List<Token> args)
    {
        var pos = args.Where(a => !a.IsOption(out _, out _)).ToList();
        var opts = new Dictionary<string, string>();
        foreach (var a in args)
            if (a.IsOption(out var k, out var v)) opts[k] = v;

        int OptInt(string key, int def) => opts.TryGetValue(key, out var v) ? Int(v) : def;
        string P(int i) => i < pos.Count ? pos[i].Text : throw new FormatException($"'{verb}' precisa de mais argumentos");
        bool Has(int i) => i < pos.Count;
        Condition WithTolerance(Condition c)
        {
            if (opts.TryGetValue("tol", out var tol)) c.Tolerance = Math.Clamp(Int(tol), 0, 255);
            return c;
        }

        switch (verb)
        {
            case "move":
            case "moveby":
                return new MoveStep
                {
                    X = Int(P(0)), Y = Int(P(1)), Relative = verb == "moveby",
                    DurationMs = OptInt("duration", 0)
                };
            case "click":
            case "doubleclick":
            {
                var c = new ClickStep();
                var i = 0;
                if (Has(0) && TryButton(pos[0].Text, out var b)) { c.Button = b; i = 1; }
                if (Has(i))
                {
                    c.UsePosition = true;
                    c.X = Int(P(i));
                    c.Y = Int(P(i + 1));
                }
                else c.UsePosition = false;
                c.Clicks = OptInt("count", verb == "doubleclick" ? 2 : 1);
                c.HoldMs = OptInt("hold", c.HoldMs);
                c.IntervalMs = OptInt("interval", c.IntervalMs);
                return c;
            }
            case "down":
            case "up":
            {
                var s = new MouseButtonStep { Action = verb == "down" ? ButtonAction.Press : ButtonAction.Release };
                var i = 0;
                if (Has(0) && TryButton(pos[0].Text, out var b)) { s.Button = b; i = 1; }
                if (Has(i))
                {
                    s.UsePosition = true;
                    s.X = Int(P(i));
                    s.Y = Int(P(i + 1));
                }
                return s;
            }
            case "path":
                return new MousePathStep { Path = ParsePath(pos) };
            case "look":
                return new MouseLookStep { Path = ParsePath(pos) };
            case "drag":
            {
                var d = new DragStep();
                var i = 0;
                if (Has(0) && TryButton(pos[0].Text, out var b)) { d.Button = b; i = 1; }
                var pathAt = pos.FindIndex(t => t.Text == "path");
                var nums = (pathAt >= 0 ? pos.Take(pathAt) : pos).Skip(i).Where(t => t.Text != "->").ToList();
                if (nums.Count < 4) throw new FormatException("'drag' precisa de X1 Y1 X2 Y2");
                d.FromX = Int(nums[0].Text); d.FromY = Int(nums[1].Text);
                d.ToX = Int(nums[2].Text); d.ToY = Int(nums[3].Text);
                d.DurationMs = OptInt("duration", d.DurationMs);
                d.HoldMs = OptInt("hold", d.HoldMs);
                d.RelativeMotion = OptInt("rel", 0) != 0;
                if (pathAt >= 0) d.Path = ParsePath(pos.Skip(pathAt + 1).ToList());
                return d;
            }
            case "scroll":
            {
                var s = new ScrollStep
                {
                    Direction = P(0).ToLowerInvariant() switch
                    {
                        "up" or "cima" => ScrollDirection.Up,
                        "down" or "baixo" => ScrollDirection.Down,
                        "left" or "esquerda" => ScrollDirection.Left,
                        "right" or "direita" => ScrollDirection.Right,
                        var x => throw new FormatException($"Direção de rolagem inválida: {x}")
                    }
                };
                var i = 1;
                if (Has(i) && pos[i].Text != "at") { s.Amount = Math.Max(1, Int(pos[i].Text)); i++; }
                if (Has(i) && pos[i].Text == "at")
                {
                    s.UsePosition = true;
                    s.X = Int(P(i + 1));
                    s.Y = Int(P(i + 2));
                }
                s.IntervalMs = OptInt("interval", s.IntervalMs);
                s.ClickFirst = OptInt("click", 0) != 0;
                return s;
            }
            case "key":
            case "keydown":
            case "keyup":
            {
                if (!Has(0)) throw new FormatException($"'{verb}' precisa da tecla");
                var k = new KeyStep
                {
                    Keys = string.Concat(pos.Select(t => t.Text)),
                    Action = verb switch { "keydown" => KeyAction.Down, "keyup" => KeyAction.Up, _ => KeyAction.Press }
                };
                if (!Input.VirtualKeys.TryParseChord(k.Keys, out var chord, out var err))
                    throw new FormatException(err ?? "tecla inválida");
                k.Keys = Input.VirtualKeys.Format(chord);
                k.HoldMs = OptInt("hold", k.HoldMs);
                k.Times = Math.Max(1, OptInt("times", k.Times));
                k.IntervalMs = OptInt("interval", k.IntervalMs);
                return k;
            }
            case "type":
                return new TypeTextStep { Text = Has(0) ? pos[0].Text : "", CharDelayMs = OptInt("delay", new TypeTextStep().CharDelayMs) };
            case "wait":
            {
                var v = P(0);
                var dash = v.IndexOf('-', 1);
                if (dash > 0)
                    return new WaitStep { Random = true, Ms = Int(v[..dash]), MaxMs = Int(v[(dash + 1)..]) };
                return new WaitStep { Ms = Int(v) };
            }
            case "if":
                return new IfStep { Condition = WithTolerance(ParseCondition(pos)) };
            case "waituntil":
            {
                var w = new WaitUntilStep { Condition = WithTolerance(ParseCondition(pos)) };
                w.TimeoutMs = OptInt("timeout", w.TimeoutMs);
                w.PollMs = OptInt("poll", w.PollMs);
                if (opts.TryGetValue("ontimeout", out var t))
                    w.OnTimeout = t.ToLowerInvariant() switch
                    {
                        "stop" => TimeoutAction.StopFlow,
                        "next" => TimeoutAction.NextIteration,
                        "continue" => TimeoutAction.Continue,
                        var x => throw new FormatException($"ontimeout inválido: {x}")
                    };
                return w;
            }
            case "repeat":
            {
                var v = Has(0) ? pos[0].Text.ToLowerInvariant() : "forever";
                return new RepeatStep { Count = v is "forever" or "infinite" or "infinito" ? 0 : Math.Max(0, Int(v)) };
            }
            case "hold":
            {
                var h = new HoldStep();
                if (Has(0) && pos[0].Text.Equals("mouse", StringComparison.OrdinalIgnoreCase))
                {
                    h.Kind = HoldInputKind.MouseButton;
                    if (Has(1) && TryButton(pos[1].Text, out var b)) h.Button = b;
                }
                else
                {
                    h.Kind = HoldInputKind.Key;
                    var i = Has(0) && pos[0].Text.Equals("key", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    if (!Has(i)) throw new FormatException("'hold' precisa da tecla (ou 'mouse <botão>')");
                    var keys = string.Concat(pos.Skip(i).Select(t => t.Text));
                    if (!Input.VirtualKeys.TryParseChord(keys, out var chord, out var err)) throw new FormatException(err ?? "tecla inválida");
                    h.Keys = Input.VirtualKeys.Format(chord);
                }
                return h;
            }
            case "parallel":
                return new ParallelStep();
            case "stop":
                return new StopStep { Mode = StopMode.StopFlow };
            case "next":
                return new StopStep { Mode = StopMode.NextIteration };
            case "break":
                return new StopStep { Mode = StopMode.BreakLoop };
            default:
                throw new FormatException($"Comando desconhecido: “{verb}”");
        }
    }

    private static Condition ParseCondition(List<Token> pos)
    {
        var c = new Condition();
        var i = 0;
        if (pos.Count > 0 && !pos[0].Quoted && pos[0].Text.Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            c.Negate = true;
            i = 1;
        }
        if (i >= pos.Count) throw new FormatException("Condição vazia");
        string A(int k) => i + k < pos.Count ? pos[i + k].Text : throw new FormatException("Condição incompleta");

        switch (pos[i].Text.ToLowerInvariant())
        {
            case "pixel":
                c.Kind = ConditionKind.PixelColor;
                c.X = Int(A(1)); c.Y = Int(A(2)); c.Color = Color(A(3));
                break;
            case "region":
                c.Kind = ConditionKind.ColorInRegion;
                c.X = Int(A(1)); c.Y = Int(A(2)); c.Width = Int(A(3)); c.Height = Int(A(4)); c.Color = Color(A(5));
                break;
            case "window":
                c.Kind = ConditionKind.WindowExists;
                c.WindowTitle = A(1);
                break;
            case "active":
                c.Kind = ConditionKind.WindowActive;
                c.WindowTitle = A(1);
                break;
            default:
                throw new FormatException($"Condição desconhecida: {pos[i].Text}");
        }
        return c;
    }

    // Quebra a linha em palavras/textos entre aspas; "// ..." no fim vira anotação.
    private static (List<Token> Tokens, string? Note) Tokenize(string line)
    {
        var tokens = new List<Token>();
        string? note = null;
        var sb = new StringBuilder();
        var quoted = false;
        var startsQuoted = false;
        var hasToken = false;

        void Flush()
        {
            if (!hasToken) return;
            tokens.Add(new Token(sb.ToString(), startsQuoted));
            sb.Clear();
            hasToken = false;
            startsQuoted = false;
        }

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '\\' && i + 1 < line.Length)
                {
                    var nx = line[++i];
                    sb.Append(nx switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => nx });
                }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                if (!hasToken) startsQuoted = true;
                hasToken = true;
                quoted = true;
            }
            else if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                note = line[(i + 2)..].Trim();
                break;
            }
            else if (char.IsWhiteSpace(ch)) Flush();
            else
            {
                hasToken = true;
                sb.Append(ch);
            }
        }
        if (quoted) throw new FormatException("Aspas sem fechar");
        Flush();
        return (tokens, note);
    }

    private static bool TryButton(string text, out MouseButtonKind button)
    {
        switch (text.ToLowerInvariant())
        {
            case "left" or "esquerdo": button = MouseButtonKind.Left; return true;
            case "right" or "direito": button = MouseButtonKind.Right; return true;
            case "middle" or "meio": button = MouseButtonKind.Middle; return true;
            case "x1": button = MouseButtonKind.X1; return true;
            case "x2": button = MouseButtonKind.X2; return true;
            default: button = MouseButtonKind.Left; return false;
        }
    }

    private static int Int(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new FormatException($"Número inválido: “{s}”");

    private static string Color(string s) =>
        Capture.RgbColor.TryParse(s, out var c) ? c.ToHex() : throw new FormatException($"Cor inválida: “{s}”");
}
