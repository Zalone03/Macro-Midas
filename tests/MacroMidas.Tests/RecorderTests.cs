using MacroMidas.Hooks;
using MacroMidas.Models;

namespace MacroMidas.Tests;

public class RecorderTests
{
    private static readonly RecordingSettings S = new() { MinWaitMs = 40, DragThreshold = 6, LongPressMs = 450 };

    private static HookEvent Down(long t, int x, int y, MouseButtonKind b = MouseButtonKind.Left) => new(HookEventKind.MouseDown, t, x, y, b);
    private static HookEvent Up(long t, int x, int y, MouseButtonKind b = MouseButtonKind.Left) => new(HookEventKind.MouseUp, t, x, y, b);
    private static HookEvent KDown(long t, ushort vk) => new(HookEventKind.KeyDown, t, 0, 0, Vk: vk);
    private static HookEvent KUp(long t, ushort vk) => new(HookEventKind.KeyUp, t, 0, 0, Vk: vk);
    private static HookEvent Wheel(long t, int x, int y, ScrollDirection d) => new(HookEventKind.Wheel, t, x, y, Wheel: d);
    private static HookEvent Move(long t, int x, int y) => new(HookEventKind.MouseMove, t, x, y);
    private static HookEvent Rel(long t, int dx, int dy) => new(HookEventKind.RelMove, t, dx, dy);

    [Fact]
    public void Turning_the_camera_with_the_right_button_is_recorded_in_order()
    {
        // Jogo: segura o botão direito (o cursor some e fica preso), gira a câmera, anda com W e solta.
        const ushort w = 0x57;
        var steps = Recorder.BuildSteps(
        [
            Down(0, 849, 462, MouseButtonKind.Right),
            Rel(100, 5, 0), Rel(108, 6, -1), Rel(116, 4, 0),
            KDown(300, w), KUp(500, w),
            Up(900, 849, 462, MouseButtonKind.Right),
        ], S, 500);

        var press = Assert.IsType<MouseButtonStep>(steps[0]);
        Assert.Equal((ButtonAction.Press, MouseButtonKind.Right), (press.Action, press.Button));
        Assert.Equal(100, Assert.IsType<WaitStep>(steps[1]).Ms);
        Assert.Equal([0, 5, 0, 8, 6, -1, 8, 4, 0], Assert.IsType<MouseLookStep>(steps[2]).Path);
        Assert.Equal(184, Assert.IsType<WaitStep>(steps[3]).Ms);
        Assert.Equal(("W", KeyAction.Press), (((KeyStep)steps[4]).Keys, ((KeyStep)steps[4]).Action));
        Assert.Equal(400, Assert.IsType<WaitStep>(steps[5]).Ms);
        var release = Assert.IsType<MouseButtonStep>(steps[6]);
        Assert.Equal((ButtonAction.Release, MouseButtonKind.Right), (release.Action, release.Button));
    }

    [Fact]
    public void A_quick_right_click_while_turning_is_not_turned_into_a_plain_click()
    {
        var steps = Recorder.BuildSteps(
            [Down(0, 10, 10, MouseButtonKind.Right), Rel(20, 30, 0), Up(120, 10, 10, MouseButtonKind.Right)], S, 500);
        Assert.Equal(ButtonAction.Press, Assert.IsType<MouseButtonStep>(steps[0]).Action);
        Assert.Contains(steps, st => st is MouseLookStep);
        Assert.Equal(ButtonAction.Release, Assert.IsType<MouseButtonStep>(steps[^1]).Action);
    }

    [Fact]
    public void Every_mouse_move_becomes_one_exact_path_before_the_click()
    {
        var steps = Recorder.BuildSteps(
        [
            Move(0, 10, 10), Move(8, 12, 11), Move(16, 15, 13), Move(24, 15, 14),
            Down(100, 15, 14), Up(150, 15, 14),
        ], S, 500);

        var path = Assert.IsType<MousePathStep>(steps[0]);
        Assert.Equal([0, 10, 10, 8, 12, 11, 8, 15, 13, 8, 15, 14], path.Path);
        Assert.Equal(76, Assert.IsType<WaitStep>(steps[1]).Ms);
        var click = Assert.IsType<ClickStep>(steps[2]);
        Assert.Equal((15, 14), (click.X, click.Y));
        Assert.Equal(3, steps.Count);
    }

    [Fact]
    public void Drag_keeps_the_whole_trajectory_even_when_it_ends_where_it_started()
    {
        var steps = Recorder.BuildSteps(
        [
            Down(0, 10, 10), Move(10, 40, 10), Move(20, 40, 40), Move(30, 10, 10), Up(40, 10, 10),
        ], S, 500);

        var drag = Assert.IsType<DragStep>(Assert.Single(steps));
        Assert.Equal([10, 40, 10, 10, 40, 40, 10, 10, 10], drag.Path);
        Assert.Equal((10, 10, 10, 10, 40), (drag.FromX, drag.FromY, drag.ToX, drag.ToY, drag.DurationMs));
    }

    [Fact]
    public void Clicking_while_holding_a_key_records_down_clicks_up_in_order()
    {
        // Segura W (andar) e clica duas vezes durante: vira Segurar W … cliques … Soltar W.
        const ushort w = 0x57;
        var steps = Recorder.BuildSteps(
        [
            KDown(0, w),
            Down(100, 5, 5), Up(150, 5, 5),
            Down(300, 5, 5), Up(350, 5, 5),
            KUp(600, w),
        ], S, doubleClickMs: 0);

        Assert.Equal(("W", KeyAction.Down), (((KeyStep)steps[0]).Keys, ((KeyStep)steps[0]).Action));
        Assert.Equal(2, steps.OfType<ClickStep>().Count());
        Assert.Equal(KeyAction.Up, ((KeyStep)steps[^1]).Action);
    }

    [Fact]
    public void Moving_the_mouse_while_holding_a_key_keeps_both_in_order()
    {
        const ushort w = 0x57;
        var steps = Recorder.BuildSteps([KDown(0, w), Move(50, 1, 1), Move(60, 2, 2), KUp(300, w)], S, 500);

        Assert.Equal(("W", KeyAction.Down), (Assert.IsType<KeyStep>(steps[0]).Keys, ((KeyStep)steps[0]).Action));
        Assert.Equal(50, Assert.IsType<WaitStep>(steps[1]).Ms);
        Assert.Equal([0, 1, 1, 10, 2, 2], Assert.IsType<MousePathStep>(steps[2]).Path);
        Assert.Equal(240, Assert.IsType<WaitStep>(steps[3]).Ms);
        Assert.Equal(KeyAction.Up, Assert.IsType<KeyStep>(steps[4]).Action);
    }

    [Fact]
    public void Short_pauses_are_not_lost_so_the_timing_does_not_drift()
    {
        var steps = Recorder.BuildSteps(
        [
            Down(0, 1, 1), Up(20, 1, 1), Move(30, 2, 2), Move(40, 3, 3), Down(1000, 3, 3), Up(1020, 3, 3),
        ], S, doubleClickMs: 0);
        var path = Assert.IsType<MousePathStep>(steps[1]);
        Assert.Equal(10, path.Path[0]);
        Assert.Equal(960, Assert.IsType<WaitStep>(steps[2]).Ms);
    }

    [Fact]
    public void Trailing_moves_to_the_finish_button_can_be_trimmed()
    {
        var events = new List<HookEvent> { Down(0, 1, 1), Up(20, 1, 1), Move(30, 200, 900), Move(40, 400, 950) };
        Recorder.TrimTrailingMoves(events);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Click_then_wait_then_double_click()
    {
        var steps = Recorder.BuildSteps(
        [
            Down(0, 100, 100), Up(60, 101, 100),
            Down(1000, 300, 300), Up(1050, 300, 300), Down(1150, 301, 300), Up(1200, 301, 300),
        ], S, doubleClickMs: 500);

        var c1 = Assert.IsType<ClickStep>(steps[0]);
        Assert.Equal((100, 100, 1), (c1.X, c1.Y, c1.Clicks));
        Assert.Equal(940, Assert.IsType<WaitStep>(steps[1]).Ms);
        var c2 = Assert.IsType<ClickStep>(steps[2]);
        Assert.Equal(2, c2.Clicks);
        Assert.Equal(3, steps.Count);
    }

    [Fact]
    public void Drag_long_press_and_scroll_merge()
    {
        var steps = Recorder.BuildSteps(
        [
            Down(0, 10, 10), Up(600, 400, 300),
            Down(1000, 50, 50), Up(1700, 52, 50),
            Wheel(2000, 5, 5, ScrollDirection.Down), Wheel(2050, 5, 5, ScrollDirection.Down), Wheel(2100, 6, 5, ScrollDirection.Down),
        ], S, doubleClickMs: 500);

        var drag = Assert.IsType<DragStep>(steps[0]);
        Assert.Equal((10, 10, 400, 300), (drag.FromX, drag.FromY, drag.ToX, drag.ToY));
        Assert.Equal(600, drag.DurationMs + drag.HoldMs);

        Assert.IsType<WaitStep>(steps[1]);
        var press = Assert.IsType<MouseButtonStep>(steps[2]);
        Assert.Equal(ButtonAction.Press, press.Action);
        Assert.Equal(700, Assert.IsType<WaitStep>(steps[3]).Ms);
        Assert.Equal(ButtonAction.Release, Assert.IsType<MouseButtonStep>(steps[4]).Action);
        Assert.IsType<WaitStep>(steps[5]);
        var scroll = Assert.IsType<ScrollStep>(steps[6]);
        Assert.Equal(3, scroll.Amount);
        Assert.Equal(7, steps.Count);
    }

    [Fact]
    public void Keys_with_modifiers_and_modifier_alone()
    {
        const ushort lctrl = 0xA2, c = 0x43, lshift = 0xA0, a = 0x41;
        var steps = Recorder.BuildSteps(
        [
            KDown(0, lctrl), KDown(50, c), KDown(80, c) , KUp(100, c), KUp(130, lctrl),
            KDown(500, lshift), KUp(560, lshift),
            KDown(1000, a), KUp(1600, a),
        ], S, doubleClickMs: 500);

        Assert.Equal("Ctrl+C", Assert.IsType<KeyStep>(steps[0]).Keys);
        Assert.IsType<WaitStep>(steps[1]);
        var shift = Assert.IsType<KeyStep>(steps[2]);
        Assert.Equal(("Shift", KeyAction.Press), (shift.Keys, shift.Action));
        Assert.IsType<WaitStep>(steps[3]);
        var hold = Assert.IsType<KeyStep>(steps[4]);
        Assert.Equal(("A", KeyAction.Down), (hold.Keys, hold.Action));
        Assert.Equal(600, Assert.IsType<WaitStep>(steps[5]).Ms);
        Assert.Equal(KeyAction.Up, Assert.IsType<KeyStep>(steps[6]).Action);
    }

    [Fact]
    public void Trailing_modifier_from_stop_hotkey_is_removed()
    {
        var steps = Recorder.BuildSteps([Down(0, 1, 1), Up(30, 1, 1), KDown(500, 0xA2), KUp(520, 0xA2)], S, 500);
        Assert.Single(steps);
        Assert.IsType<ClickStep>(steps[0]);
    }
}
