using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Serialization;

namespace MacroMidas.Tests;

public class SerializationTests
{
    internal static Flow SampleFlow()
    {
        var flow = new Flow { Name = "Teste \"completo\"", Description = "Todos os tipos de passo" };
        flow.Settings.RepeatMode = RepeatMode.Infinite;
        flow.Settings.IterationDelayMs = 250;
        flow.Settings.Speed = 1.5;
        flow.Settings.Target = TargetKind.Window;
        flow.Settings.WindowTitle = "Bloco de Notas";
        flow.Settings.ProcessName = "notepad.exe";
        flow.Settings.InputMode = InputMode.Background;
        flow.Settings.Hotkey = "Ctrl+F1";
        flow.Settings.CoordinateMode = CoordinateMode.Proportional;
        flow.Settings.AuthorScreenWidth = 1920;
        flow.Settings.AuthorScreenHeight = 1080;

        flow.Steps.Add(new MoveStep { X = 10, Y = 20, DurationMs = 150 });
        flow.Steps.Add(new MoveStep { X = -5, Y = 7, Relative = true });
        flow.Steps.Add(new ClickStep { Button = MouseButtonKind.Right, X = 100, Y = 200, Clicks = 2, HoldMs = 45, Note = "abre o menu" });
        flow.Steps.Add(new ClickStep { UsePosition = false, Enabled = false });
        flow.Steps.Add(new MouseButtonStep { Action = ButtonAction.Press, UsePosition = true, X = 1, Y = 2 });
        flow.Steps.Add(new MouseButtonStep { Action = ButtonAction.Release, Button = MouseButtonKind.Middle });
        flow.Steps.Add(new DragStep { FromX = 1, FromY = 2, ToX = 300, ToY = 400, DurationMs = 700, HoldMs = 20 });
        flow.Steps.Add(new ScrollStep { Direction = ScrollDirection.Left, Amount = 4, UsePosition = true, X = 9, Y = 8, IntervalMs = 15 });
        flow.Steps.Add(new ScrollStep { Direction = ScrollDirection.Down, Amount = 2, UsePosition = true, X = 50, Y = 60, ClickFirst = true });
        flow.Steps.Add(new MousePathStep { Path = [0, 10, 10, 8, 12, -3, 16, 15, 13] });
        flow.Steps.Add(new MouseLookStep { Path = [0, 5, -2, 4, 7, 0, 4, -3, 1] });
        flow.Steps.Add(new DragStep { FromX = 5, FromY = 5, ToX = 40, ToY = 9, DurationMs = 90, HoldMs = 0, RelativeMotion = true, Path = [12, 20, 6, 30, 40, 9] });
        flow.Steps.Add(new KeyStep { Keys = "Ctrl+Shift+S", Times = 3, IntervalMs = 80 });
        flow.Steps.Add(new KeyStep { Keys = "Shift", Action = KeyAction.Down });
        flow.Steps.Add(new TypeTextStep { Text = "Olá \"mundo\"\nlinha 2\t✨ // não é nota", CharDelayMs = 5 });
        flow.Steps.Add(new WaitStep { Ms = 300, Random = true, MaxMs = 800 });

        var ifStep = new IfStep
        {
            Condition = new Condition { Kind = ConditionKind.PixelColor, X = 5, Y = 6, Color = "#FFD54A", Tolerance = 12, Negate = true }
        };
        ifStep.Then.Add(new ClickStep { X = 5, Y = 6 });
        ifStep.Else.Add(new StopStep { Mode = StopMode.NextIteration });
        flow.Steps.Add(ifStep);

        var hold = new HoldStep { Kind = HoldInputKind.Key, Keys = "W" };
        hold.Body.Add(new ClickStep { X = 10, Y = 10 });
        hold.Body.Add(new WaitStep { Ms = 50 });
        flow.Steps.Add(hold);
        var holdMouse = new HoldStep { Kind = HoldInputKind.MouseButton, Button = MouseButtonKind.Right };
        holdMouse.Body.Add(new KeyStep { Keys = "A" });
        flow.Steps.Add(holdMouse);

        var repeat = new RepeatStep { Count = 0 };
        repeat.Body.Add(new WaitUntilStep
        {
            Condition = new Condition { Kind = ConditionKind.ColorInRegion, X = 1, Y = 2, Width = 30, Height = 40, Color = "#112233" },
            TimeoutMs = 5000,
            OnTimeout = TimeoutAction.StopFlow
        });
        var inner = new IfStep { Condition = new Condition { Kind = ConditionKind.WindowExists, WindowTitle = "Pronto" } };
        inner.Then.Add(new StopStep { Mode = StopMode.BreakLoop });
        repeat.Body.Add(inner);
        flow.Steps.Add(repeat);

        var parallel = new ParallelStep();
        parallel.Tracks[0].Steps.Add(new KeyStep { Keys = "W", Action = KeyAction.Down });
        parallel.Tracks[0].Steps.Add(new WaitStep { Ms = 120 });
        parallel.Tracks[1].Steps.Add(new ClickStep { X = 7, Y = 8, Clicks = 2 });
        parallel.AddTrack();
        parallel.Tracks[2].Steps.Add(new MoveStep { X = 3, Y = 4, Relative = true });
        flow.Steps.Add(parallel);

        flow.Steps.Add(new StopStep());
        return flow;
    }

    [Fact]
    public void Mmf_round_trip_preserves_everything()
    {
        var flow = SampleFlow();
        var json = MmfSerializer.Serialize(flow);
        var back = MmfSerializer.Deserialize(json);
        Assert.Equal(json, MmfSerializer.Serialize(back));
        Assert.Contains("\"$type\": \"if\"", json);
        Assert.DoesNotContain("\"summary\"", json);
        Assert.DoesNotContain("\"title\"", json);
        Assert.Equal(Flow.CountSteps(flow.Steps), Flow.CountSteps(back.Steps));
    }

    [Fact]
    public void Mmf_rejects_other_files()
    {
        Assert.Throws<FormatException>(() => MmfSerializer.Deserialize("{\"format\":\"outra-coisa\"}"));
        Assert.Throws<FormatException>(() => MmfSerializer.Deserialize("isso não é json"));
    }

    [Fact]
    public void Txt_round_trip_is_stable_and_equivalent()
    {
        var flow = SampleFlow();
        var txt = TxtFlowWriter.Write(flow);
        var parsed = TxtFlowParser.Parse(txt);
        Assert.Equal(txt, TxtFlowWriter.Write(parsed));

        parsed.Id = flow.Id;
        parsed.CreatedAt = flow.CreatedAt;
        parsed.ModifiedAt = flow.ModifiedAt;
        Assert.Equal(MmfSerializer.Serialize(flow), MmfSerializer.Serialize(parsed));
    }

    [Fact]
    public void Txt_parses_handwritten_script()
    {
        const string text = """
            # comentário
            @name "Meu fluxo"
            @repeat 5

            click 100 200
            doubleclick right
            off: wait 300-800   // aleatório e desativado
            key ctrl + c
            drag 1 2 -> 30 40 duration=900
            scroll down 3 at 50 60
            if not pixel 10 20 #ABC tol=30
              type "oi\n"
            else
              stop
            end
            repeat forever
              waituntil window "Calc" timeout=2000 ontimeout=next
              break
            end
            """;
        var flow = TxtFlowParser.Parse(text);
        Assert.Equal("Meu fluxo", flow.Name);
        Assert.Equal(5, flow.Settings.RepeatCount);
        Assert.Equal(8, flow.Steps.Count);

        var dbl = Assert.IsType<ClickStep>(flow.Steps[1]);
        Assert.Equal(2, dbl.Clicks);
        Assert.False(dbl.UsePosition);
        Assert.Equal(MouseButtonKind.Right, dbl.Button);

        var wait = Assert.IsType<WaitStep>(flow.Steps[2]);
        Assert.False(wait.Enabled);
        Assert.True(wait.Random);
        Assert.Equal((300, 800), (wait.Ms, wait.MaxMs));
        Assert.Equal("aleatório e desativado", wait.Note);

        Assert.Equal("Ctrl+C", Assert.IsType<KeyStep>(flow.Steps[3]).Keys);
        var drag = Assert.IsType<DragStep>(flow.Steps[4]);
        Assert.Equal((1, 2, 30, 40, 900), (drag.FromX, drag.FromY, drag.ToX, drag.ToY, drag.DurationMs));

        var f = Assert.IsType<IfStep>(flow.Steps[6]);
        Assert.True(f.Condition.Negate);
        Assert.Equal("#AABBCC", f.Condition.Color);
        Assert.Equal(30, f.Condition.Tolerance);
        Assert.Equal("oi\n", Assert.IsType<TypeTextStep>(f.Then[0]).Text);
        Assert.IsType<StopStep>(f.Else[0]);

        var rep = Assert.IsType<RepeatStep>(flow.Steps[7]);
        Assert.Equal(0, rep.Count);
        var wu = Assert.IsType<WaitUntilStep>(rep.Body[0]);
        Assert.Equal(TimeoutAction.NextIteration, wu.OnTimeout);
        Assert.Equal(StopMode.BreakLoop, Assert.IsType<StopStep>(rep.Body[1]).Mode);
    }

    [Fact]
    public void Readme_example_script_parses()
    {
        var readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "README.md"));
        var start = readme.IndexOf("```text", StringComparison.Ordinal) + "```text".Length;
        var end = readme.IndexOf("```", start, StringComparison.Ordinal);
        var flow = TxtFlowParser.Parse(readme[start..end]);
        Assert.Contains(Flow.Walk(flow.Steps), s => s is ScrollStep { ClickFirst: true });
        Assert.Contains(Flow.Walk(flow.Steps), s => s is MousePathStep { PointCount: 3 });
    }

    [Fact]
    public void Txt_reports_errors_with_line_numbers()
    {
        var ex = Assert.Throws<FlowParseException>(() => TxtFlowParser.Parse("click 1 2\nvoar 3\nif pixel 1 2 #FFF\n"));
        Assert.Contains(ex.Errors, e => e.StartsWith("Linha 2:"));
        Assert.Contains(ex.Errors, e => e.StartsWith("Linha 3:") && e.Contains("sem 'end'"));
    }

    [Theory]
    [InlineData("ctrl + shift + s", "Ctrl+Shift+S")]
    [InlineData("alt+f4", "Alt+F4")]
    [InlineData("Ctrl++", "Ctrl+OemPlus")]
    [InlineData("shift", "Shift")]
    [InlineData("Win+D", "Win+D")]
    [InlineData("numpad5", "Numpad5")]
    [InlineData("VK7B", "F12")]
    public void Key_chords_are_normalized(string input, string expected) => Assert.Equal(expected, VirtualKeys.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Banana")]
    [InlineData("A+B")]
    public void Invalid_chords_are_rejected(string input) => Assert.False(VirtualKeys.TryParseChord(input, out _, out _));
}
