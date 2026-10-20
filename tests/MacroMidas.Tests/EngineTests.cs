using MacroMidas.Capture;
using MacroMidas.Engine;
using MacroMidas.Models;

namespace MacroMidas.Tests;

public class EngineTests
{
    private static Flow FlowOf(params FlowStep[] steps)
    {
        var f = new Flow { Name = "t" };
        foreach (var s in steps) f.Steps.Add(s);
        return f;
    }

    [Fact]
    public void Runs_steps_in_order_with_mouse_keyboard_and_scroll()
    {
        var flow = FlowOf(
            new ClickStep { X = 10, Y = 20, HoldMs = 1 },
            new KeyStep { Keys = "Ctrl+C", HoldMs = 1 },
            new ScrollStep { Direction = ScrollDirection.Down, Amount = 2, IntervalMs = 1 },
            new TypeTextStep { Text = "oi", CharDelayMs = 0 });

        var (_, input, info) = Runs.Execute(flow);

        Assert.Equal(RunEndReason.Completed, info.Reason);
        Assert.Equal(
        [
            "move 10,20", "down Left", "up Left",
            "keydown Ctrl", "keydown C", "keyup C", "keyup Ctrl",
            "wheel Down", "wheel Down",
            "type o", "type i"
        ], input.Events);
    }

    [Fact]
    public void Double_click_marks_the_second_press()
    {
        var (_, input, _) = Runs.Execute(FlowOf(new ClickStep { UsePosition = false, Clicks = 2, HoldMs = 1, IntervalMs = 1 }));
        Assert.Equal(["down Left", "up Left", "down Left dbl", "up Left"], input.Events);
    }

    [Fact]
    public void Drag_holds_button_while_moving()
    {
        var (_, input, _) = Runs.Execute(FlowOf(new DragStep { FromX = 0, FromY = 0, ToX = 100, ToY = 0, DurationMs = 40, HoldMs = 1 }));
        var ev = input.Events;
        var down = ev.ToList().IndexOf("down Left");
        var up = ev.ToList().IndexOf("up Left");
        Assert.True(down > 0 && up > down);
        Assert.True(up - down > 2, "precisa de movimentos intermediários durante o arrasto");
        Assert.Equal("move 100,0", ev[up - 1]);
    }

    [Fact]
    public void Scroll_goes_to_the_point_clicks_first_then_rolls()
    {
        var (_, input, _) = Runs.Execute(FlowOf(new ScrollStep
            { UsePosition = true, X = 40, Y = 50, ClickFirst = true, Amount = 2, IntervalMs = 1 }));
        Assert.Equal(["move 40,50", "down Left", "up Left", "wheel Down", "wheel Down"], input.Events);
    }

    [Fact]
    public void Recorded_path_is_replayed_point_by_point_in_time()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (_, input, _) = Runs.Execute(FlowOf(new MousePathStep { Path = [0, 1, 1, 40, 5, 3, 40, 9, 7] }));
        Assert.Equal(["move 1,1", "move 5,3", "move 9,7"], input.Events);
        Assert.True(sw.ElapsedMilliseconds >= 75, $"respeita os tempos gravados ({sw.ElapsedMilliseconds} ms)");
    }

    [Fact]
    public void Relative_drag_moves_by_offsets_so_games_recognize_it()
    {
        var (_, input, _) = Runs.Execute(FlowOf(
            new DragStep { FromX = 0, FromY = 0, ToX = 40, ToY = 0, DurationMs = 40, HoldMs = 1, RelativeMotion = true }));
        var ev = input.Events;
        Assert.Equal("move 0,0", ev[0]);
        Assert.Equal("down Left", ev[1]);
        Assert.Contains(ev, e => e.StartsWith("rel "));
        Assert.DoesNotContain(ev.Skip(2), e => e.StartsWith("move "));
        Assert.Equal("up Left", ev[^1]);
    }

    [Fact]
    public void Recorded_drag_with_relative_motion_replays_as_offsets()
    {
        var d = new DragStep { FromX = 0, FromY = 0, ToX = 30, ToY = 0, DurationMs = 20, RelativeMotion = true, Path = [0, 0, 0, 5, 10, 0, 5, 30, 0] };
        var ev = Runs.Execute(FlowOf(d)).Input.Events.ToList();
        Assert.Equal("move 0,0", ev[0]);
        Assert.Equal("down Left", ev[1]);
        Assert.Contains(ev, e => e.StartsWith("rel "));
        Assert.DoesNotContain(ev.Skip(2), e => e.StartsWith("move "));
        Assert.Equal("up Left", ev[^1]);
    }

    [Fact]
    public void Hold_key_block_keeps_key_down_during_a_drag_inside()
    {
        var hold = new HoldStep { Kind = HoldInputKind.Key, Keys = "W" };
        hold.Body.Add(new DragStep { FromX = 0, FromY = 0, ToX = 20, ToY = 0, DurationMs = 20, HoldMs = 1 });
        var ev = Runs.Execute(FlowOf(hold)).Input.Events.ToList();
        Assert.Equal("keydown W", ev[0]);
        Assert.Equal("keyup W", ev[^1]);
        Assert.Contains("down Left", ev);
        Assert.True(ev.IndexOf("up Left") < ev.LastIndexOf("keyup W"));
    }

    [Fact]
    public void Hold_block_keeps_input_down_while_inner_steps_run()
    {
        var hold = new HoldStep { Kind = HoldInputKind.Key, Keys = "W" };
        hold.Body.Add(new ClickStep { UsePosition = false, HoldMs = 1 });
        hold.Body.Add(new ClickStep { UsePosition = false, HoldMs = 1 });
        var (_, input, _) = Runs.Execute(FlowOf(hold));
        Assert.Equal(["keydown W", "down Left", "up Left", "down Left", "up Left", "keyup W"], input.Events);

        var holdMouse = new HoldStep { Kind = HoldInputKind.MouseButton, Button = MouseButtonKind.Right };
        holdMouse.Body.Add(new KeyStep { Keys = "A", HoldMs = 1 });
        var (_, m, _) = Runs.Execute(FlowOf(holdMouse));
        Assert.Equal(["down Right", "keydown A", "keyup A", "up Right"], m.Events);
    }

    [Fact]
    public void Parallel_runs_tracks_at_the_same_time()
    {
        var p = new ParallelStep();
        p.Tracks[0].Steps.Add(new WaitStep { Ms = 400 });
        p.Tracks[0].Steps.Add(new KeyStep { Keys = "A", HoldMs = 1 });
        p.Tracks[1].Steps.Add(new WaitStep { Ms = 400 });
        p.Tracks[1].Steps.Add(new KeyStep { Keys = "B", HoldMs = 1 });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (_, input, info) = Runs.Execute(FlowOf(p));
        Assert.Equal(RunEndReason.Completed, info.Reason);
        Assert.Contains("keydown A", input.Events);
        Assert.Contains("keydown B", input.Events);
        Assert.True(sw.ElapsedMilliseconds < 650, $"demorou {sw.ElapsedMilliseconds} ms (não ficou paralelo)");
    }

    [Fact]
    public void Parallel_infinite_tracks_stop_together_on_request()
    {
        var p = new ParallelStep();
        var r0 = new RepeatStep { Count = 0 };
        r0.Body.Add(new KeyStep { Keys = "A", HoldMs = 1 });
        r0.Body.Add(new WaitStep { Ms = 5 });
        var r1 = new RepeatStep { Count = 0 };
        r1.Body.Add(new KeyStep { Keys = "B", HoldMs = 1 });
        r1.Body.Add(new WaitStep { Ms = 5 });
        p.Tracks[0].Steps.Add(r0);
        p.Tracks[1].Steps.Add(r1);

        var (_, input, info) = Runs.Execute(FlowOf(p),
            during: r => { System.Threading.Thread.Sleep(80); r.RequestStop(); }, timeoutMs: 5000);
        Assert.Contains(info.Reason, new[] { RunEndReason.Stopped, RunEndReason.Completed });
        Assert.Contains("keydown A", input.Events);
        Assert.Contains("keydown B", input.Events);
    }

    [Fact]
    public void Hold_block_releases_even_when_an_inner_step_stops_the_flow()
    {
        var hold = new HoldStep { Kind = HoldInputKind.Key, Keys = "W" };
        hold.Body.Add(new StopStep { Mode = StopMode.StopFlow });
        var (_, input, _) = Runs.Execute(FlowOf(hold));
        Assert.Equal(["keydown W", "keyup W"], input.Events);
    }

    [Fact]
    public void Key_and_button_hold_durations_are_respected()
    {
        var swK = System.Diagnostics.Stopwatch.StartNew();
        var (_, k, _) = Runs.Execute(FlowOf(new KeyStep { Keys = "W", HoldMs = 160 }));
        Assert.Equal(["keydown W", "keyup W"], k.Events);
        Assert.True(swK.ElapsedMilliseconds >= 130, $"segurou só {swK.ElapsedMilliseconds} ms");

        var swM = System.Diagnostics.Stopwatch.StartNew();
        var (_, m, _) = Runs.Execute(FlowOf(new ClickStep { UsePosition = false, HoldMs = 160 }));
        Assert.Equal(["down Left", "up Left"], m.Events);
        Assert.True(swM.ElapsedMilliseconds >= 130, $"segurou só {swM.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void AutoClick_template_uses_real_input_so_it_works_in_games()
    {
        var f = MacroMidas.Services.FlowTemplates.AutoClick();
        Assert.Equal(InputMode.Direct, f.Settings.InputMode);
        Assert.Equal(RepeatMode.Infinite, f.Settings.RepeatMode);
    }

    [Fact]
    public void CoordinateMap_converts_pixels_to_0_1000_and_back()
    {
        var space = CoordinateSpace.Proportional(0, 0, 1920, 1080);
        var click = new ClickStep { X = 960, Y = 540 };
        var drag = new DragStep { FromX = 192, FromY = 108, ToX = 1728, ToY = 972, Path = [0, 960, 540, 10, 1728, 972] };
        var region = new IfStep { Condition = new Condition { Kind = ConditionKind.ColorInRegion, X = 192, Y = 108, Width = 384, Height = 216 } };
        var steps = new List<FlowStep> { click, drag, region };

        CoordinateMap.Convert(steps, space.ToFlow, space.DeltaToFlow);
        Assert.Equal((500, 500), (click.X, click.Y));
        Assert.Equal((100, 100, 900, 900), (drag.FromX, drag.FromY, drag.ToX, drag.ToY));
        Assert.Equal([0, 500, 500, 10, 900, 900], drag.Path);
        Assert.Equal((100, 100, 200, 200), (region.Condition.X, region.Condition.Y, region.Condition.Width, region.Condition.Height));

        CoordinateMap.Convert(steps, space.ToDevice, space.DeltaToDevice);
        Assert.Equal((960, 540), (click.X, click.Y));
        Assert.Equal((192, 108, 1728, 972), (drag.FromX, drag.FromY, drag.ToX, drag.ToY));
    }

    [Fact]
    public void Proportional_coords_land_at_the_right_pixel()
    {
        var space = CoordinateSpace.Proportional(0, 0, 2000, 1000);
        var (_, input, _) = Runs.Execute(FlowOf(
            new ClickStep { X = 500, Y = 500, HoldMs = 1 },
            new ClickStep { X = 1000, Y = 1000, HoldMs = 1 }), space: space);
        Assert.Equal("move 1000,500", input.Events[0]);
        Assert.Equal("move 2000,1000", input.Events[3]);
    }

    [Fact]
    public void Proportional_region_condition_scales_point_and_size()
    {
        var space = CoordinateSpace.Proportional(0, 0, 2000, 1000);
        var pixels = new FakePixels { Pixel = (x, y) => x == 1000 && y == 500 ? new RgbColor(255, 213, 74) : new RgbColor(0, 0, 0) };
        var inside = new Condition { Kind = ConditionKind.ColorInRegion, X = 400, Y = 400, Width = 200, Height = 200, Color = "#FFD54A", Tolerance = 10 };
        Assert.True(ConditionEvaluator.Evaluate(inside, pixels, new FakeWindows(), out _, space));
        var outside = new Condition { Kind = ConditionKind.ColorInRegion, X = 0, Y = 0, Width = 100, Height = 100, Color = "#FFD54A", Tolerance = 10 };
        Assert.False(ConditionEvaluator.Evaluate(outside, pixels, new FakeWindows(), out _, space));
    }

    [Fact]
    public void Camera_movement_is_replayed_as_relative_mouse_moves()
    {
        var (_, input, _) = Runs.Execute(FlowOf(new MouseLookStep { Path = [0, 5, -2, 2, 7, 0, 2, -3, 1] }));
        Assert.Equal(["rel 5,-2", "rel 7,0", "rel -3,1"], input.Events);
    }

    [Fact]
    public void Drag_with_recorded_path_follows_it()
    {
        var (_, input, _) = Runs.Execute(FlowOf(new DragStep
            { FromX = 0, FromY = 0, ToX = 30, ToY = 0, DurationMs = 20, Path = [5, 10, 8, 5, 20, -4, 5, 30, 0] }));
        Assert.Equal(["move 0,0", "down Left", "move 10,8", "move 20,-4", "move 30,0", "move 30,0", "up Left"], input.Events);
    }

    [Theory]
    [InlineData(255, 213, 74, true)]
    [InlineData(250, 210, 80, true)]
    [InlineData(20, 20, 20, false)]
    public void If_pixel_chooses_branch(byte r, byte g, byte b, bool expectThen)
    {
        var f = new IfStep { Condition = new Condition { X = 5, Y = 5, Color = "#FFD54A", Tolerance = 10 } };
        f.Then.Add(new KeyStep { Keys = "A", HoldMs = 1 });
        f.Else.Add(new KeyStep { Keys = "B", HoldMs = 1 });
        var pixels = new FakePixels { Pixel = (x, y) => x == 5 && y == 5 ? new RgbColor(r, g, b) : new RgbColor(0, 0, 0) };

        var (_, input, _) = Runs.Execute(FlowOf(f), pixels);

        Assert.Contains(expectThen ? "keydown A" : "keydown B", input.Events);
        Assert.DoesNotContain(expectThen ? "keydown B" : "keydown A", input.Events);
    }

    [Fact]
    public void Negated_window_condition_works()
    {
        var f = new IfStep { Condition = new Condition { Kind = ConditionKind.WindowExists, WindowTitle = "Calc", Negate = true } };
        f.Then.Add(new KeyStep { Keys = "N", HoldMs = 1 });
        var windows = new FakeWindows();
        windows.Existing.Add("Calculadora");
        var (_, input, _) = Runs.Execute(FlowOf(f), windows: windows);
        Assert.DoesNotContain("keydown N", input.Events);
    }

    [Fact]
    public void Repeat_count_and_break()
    {
        var rep = new RepeatStep { Count = 3 };
        rep.Body.Add(new KeyStep { Keys = "X", HoldMs = 1 });
        var (_, input, _) = Runs.Execute(FlowOf(rep));
        Assert.Equal(3, input.Events.Count(e => e == "keydown X"));

        var counter = 0;
        var loop = new RepeatStep { Count = 0 };
        loop.Body.Add(new KeyStep { Keys = "Y", HoldMs = 1 });
        var check = new IfStep { Condition = new Condition { X = 0, Y = 0, Color = "#FFFFFF", Tolerance = 0 } };
        check.Then.Add(new StopStep { Mode = StopMode.BreakLoop });
        loop.Body.Add(check);
        var pixels = new FakePixels { Pixel = (_, _) => ++counter >= 4 ? new RgbColor(255, 255, 255) : new RgbColor(0, 0, 0) };
        var (_, input2, info) = Runs.Execute(FlowOf(loop, new KeyStep { Keys = "Z", HoldMs = 1 }), pixels);
        Assert.Equal(RunEndReason.Completed, info.Reason);
        Assert.Equal(4, input2.Events.Count(e => e == "keydown Y"));
        Assert.Equal("keyup Z", input2.Events[^1]);
    }

    [Fact]
    public void Wait_until_times_out_and_stops_flow()
    {
        var flow = FlowOf(
            new WaitUntilStep { Condition = new Condition { Color = "#FFFFFF", Tolerance = 0 }, TimeoutMs = 60, PollMs = 5, OnTimeout = TimeoutAction.StopFlow },
            new KeyStep { Keys = "Q", HoldMs = 1 });
        var (_, input, info) = Runs.Execute(flow);
        Assert.Equal(RunEndReason.Completed, info.Reason);
        Assert.DoesNotContain("keydown Q", input.Events);
    }

    [Fact]
    public void Repeat_count_of_flow_settings_is_respected()
    {
        var flow = FlowOf(new KeyStep { Keys = "R", HoldMs = 1 });
        flow.Settings.RepeatCount = 4;
        var (_, input, info) = Runs.Execute(flow);
        Assert.Equal(4, info.Iterations);
        Assert.Equal(4, input.Events.Count(e => e == "keydown R"));
    }

    [Fact]
    public void Graceful_stop_finishes_the_current_cycle()
    {
        var flow = FlowOf(new KeyStep { Keys = "A", HoldMs = 1 }, new WaitStep { Ms = 30 }, new KeyStep { Keys = "B", HoldMs = 1 });
        flow.Settings.RepeatMode = RepeatMode.Infinite;
        var (_, input, info) = Runs.Execute(flow, during: r =>
        {
            Thread.Sleep(80);
            Assert.Equal(StopRequestResult.Graceful, r.RequestStop());
        });
        Assert.Equal(RunEndReason.Stopped, info.Reason);
        Assert.Equal("keyup B", input.Events[^1]);
        Assert.Equal(input.Events.Count(e => e == "keydown A"), input.Events.Count(e => e == "keydown B"));
    }

    [Fact]
    public void Second_stop_kills_immediately_and_releases_held_input()
    {
        var flow = FlowOf(
            new KeyStep { Keys = "Shift", Action = KeyAction.Down },
            new MouseButtonStep { Action = ButtonAction.Press },
            new WaitStep { Ms = 5000 });
        var (_, input, info) = Runs.Execute(flow, during: r =>
        {
            Thread.Sleep(60);
            r.RequestStop();
            Assert.Equal(StopRequestResult.Killed, r.RequestStop());
        }, timeoutMs: 2000);
        Assert.Equal(RunEndReason.Killed, info.Reason);
        Assert.Contains("keyup Shift", input.Events);
        Assert.Contains("up Left", input.Events);
        Assert.Empty(input.Keys);
        Assert.Empty(input.Buttons);
    }

    [Fact]
    public void Pause_releases_held_input_and_resume_restores_it()
    {
        var flow = FlowOf(
            new MouseButtonStep { Action = ButtonAction.Press },
            new WaitStep { Ms = 150 },
            new MouseButtonStep { Action = ButtonAction.Release });
        var (_, input, info) = Runs.Execute(flow, during: r =>
        {
            Thread.Sleep(40);
            r.Pause();
            Thread.Sleep(150);
            Assert.Equal(RunState.Paused, r.State);
            r.Resume();
        });
        Assert.Equal(RunEndReason.Completed, info.Reason);
        Assert.Equal(["down Left", "up Left", "move 0,0", "down Left", "up Left"], input.Events);
    }

    [Fact]
    public void Paused_time_does_not_count_for_waits()
    {
        var flow = FlowOf(new WaitStep { Ms = 120 });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Runs.Execute(flow, during: r =>
        {
            Thread.Sleep(30);
            r.Pause();
            Thread.Sleep(200);
            r.Resume();
        });
        Assert.True(sw.ElapsedMilliseconds >= 300, $"durou só {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Invalid_key_ends_with_error()
    {
        var (_, _, info) = Runs.Execute(FlowOf(new KeyStep { Keys = "Ctrl+Banana" }));
        Assert.Equal(RunEndReason.Error, info.Reason);
        Assert.Contains("Banana", info.Error);
    }

    [Fact]
    public void Disabled_steps_are_skipped_and_edits_during_run_do_not_break()
    {
        var a = new KeyStep { Keys = "A", HoldMs = 1, Enabled = false };
        var flow = FlowOf(a, new WaitStep { Ms = 40 }, new KeyStep { Keys = "B", HoldMs = 1 });
        var (_, input, _) = Runs.Execute(flow, during: _ =>
        {
            Thread.Sleep(10);
            flow.Steps.Clear();
        });
        Assert.DoesNotContain("keydown A", input.Events);
        Assert.Contains("keydown B", input.Events);
    }
}
