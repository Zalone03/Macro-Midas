using MacroMidas.Hooks;

namespace MacroMidas.Tests;

public class GuardTests
{
    private static HookEvent KDown(long t, ushort vk, bool injected = false) => new(HookEventKind.KeyDown, t, 0, 0, Vk: vk, Injected: injected);
    private static HookEvent KUp(long t, ushort vk) => new(HookEventKind.KeyUp, t, 0, 0, Vk: vk);
    private static HookEvent Move(long t, int x, int y, bool injected = false) => new(HookEventKind.MouseMove, t, x, y, Injected: injected);

    private static (SessionGuard Guard, List<string> Log) Make(bool direct)
    {
        var g = new SessionGuard([(HotkeyAction.Start, "F6"), (HotkeyAction.Pause, "F7"), (HotkeyAction.Stop, "F8, Ctrl+Alt+S")],
            watchUser: direct, corner: direct);
        var log = new List<string>();
        g.Hotkey += a => log.Add(a.ToString());
        g.UserTookOver += () => log.Add("user");
        g.Corner += () => log.Add("corner");
        return (g, log);
    }

    [Fact]
    public void Stop_keys_work_including_extra_ones_and_combinations()
    {
        var (g, log) = Make(direct: false);
        g.Process(KDown(0, 0x77));
        g.Process(KDown(10, 0x77));
        g.Process(KUp(20, 0x77));
        g.Process(KDown(100, 0xA2));
        g.Process(KDown(110, 0xA4));
        g.Process(KDown(120, 0x53));
        Assert.Equal(["Stop", "Stop"], log);
    }

    [Fact]
    public void Lock_mode_never_reacts_to_the_user_using_the_pc()
    {
        var (g, log) = Make(direct: false);
        g.Process(Move(0, 500, 500));
        g.Process(KDown(10, 0x41));
        g.Process(Move(20, 0, 0));
        Assert.Empty(log);
    }

    [Fact]
    public void Direct_mode_gives_control_back_and_ignores_what_the_flow_simulates()
    {
        var (g, log) = Make(direct: true);
        g.Process(Move(0, 300, 300, injected: true));
        g.Process(KDown(5, 0x41, injected: true));
        Assert.Empty(log);
        g.Process(Move(10, 301, 300));
        g.Process(Move(20, 305, 300));
        g.Process(Move(2000, 0, 0));
        Assert.Equal(["user", "corner"], log);
    }
}
