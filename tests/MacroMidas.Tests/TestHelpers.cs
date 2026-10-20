using Avalonia;
using Avalonia.Headless;
using MacroMidas.Capture;
using MacroMidas.Engine;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Services;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(MacroMidas.Tests.TestAppBuilder))]
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace MacroMidas.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
}

// Pasta temporária para os dados do app durante os testes.
public static class TestData
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "MacroMidasTests", Guid.NewGuid().ToString("N"));

    public static void UseTempRoot()
    {
        AppPaths.UseRoot(Root);
        AppPaths.Ensure();
    }

    // Pasta nova e vazia, como no primeiro uso do app.
    public static void UseFreshRoot()
    {
        AppPaths.UseRoot(Path.Combine(Path.GetTempPath(), "MacroMidasTests", Guid.NewGuid().ToString("N")));
        AppPaths.Ensure();
    }
}

// Mouse/teclado falsos: registram o que o motor mandou fazer.
public sealed class FakeInput : InputBackendBase
{
    private readonly object _lock = new();
    private readonly List<string> _events = [];
    private int _x, _y;

    public override string Name => "fake";

    public IReadOnlyList<string> Events
    {
        get { lock (_lock) return [.. _events]; }
    }

    public IReadOnlyCollection<MouseButtonKind> Buttons => HeldButtons;
    public IReadOnlyCollection<ushort> Keys => HeldKeys;

    private void Add(string e)
    {
        lock (_lock) _events.Add(e);
    }

    public override (int X, int Y) GetPosition() => (_x, _y);

    public override void MoveTo(int x, int y)
    {
        _x = x;
        _y = y;
        Add($"move {x},{y}");
    }

    public override void MoveBy(int dx, int dy) => Add($"rel {dx},{dy}");

    protected override void RawButton(MouseButtonKind button, bool down, bool doubleClick) =>
        Add($"{(down ? "down" : "up")} {button}{(doubleClick ? " dbl" : "")}");

    protected override void RawKey(ushort vk, bool down) => Add($"key{(down ? "down" : "up")} {VirtualKeys.NameOf(vk)}");

    public override void Wheel(ScrollDirection direction) => Add($"wheel {direction}");

    public override void TypeChar(string grapheme) => Add($"type {grapheme}");
}

public sealed class FakePixels : IPixelReader
{
    public Func<int, int, RgbColor?> Pixel { get; set; } = (_, _) => new RgbColor(0, 0, 0);

    public string Describe => "fake";

    public RgbColor? GetPixel(int x, int y) => Pixel(x, y);

    public bool FindColor(int x, int y, int width, int height, RgbColor target, int tolerance, out int foundX, out int foundY)
    {
        for (var yy = y; yy < y + height; yy++)
        for (var xx = x; xx < x + width; xx++)
            if (Pixel(xx, yy) is { } c && c.Matches(target, tolerance))
            {
                foundX = xx;
                foundY = yy;
                return true;
            }
        foundX = foundY = -1;
        return false;
    }
}

public sealed class FakeWindows : IWindowQuery
{
    public HashSet<string> Existing { get; } = [];
    public string Active { get; set; } = "";
    public bool Exists(string titleContains) => Existing.Any(t => t.Contains(titleContains, StringComparison.OrdinalIgnoreCase));
    public bool IsActive(string titleContains) => Active.Contains(titleContains, StringComparison.OrdinalIgnoreCase);
}

public static class Runs
{
    public static (FlowRunner Runner, FakeInput Input, RunFinishedInfo Info) Execute(Flow flow, FakePixels? pixels = null,
        FakeWindows? windows = null, Action<FlowRunner>? during = null, int timeoutMs = 10000, CoordinateSpace space = default)
    {
        flow.Settings.StartDelayMs = 0;
        var input = new FakeInput();
        var runner = new FlowRunner();
        RunFinishedInfo? info = null;
        runner.Finished += i => info = i;
        var env = new RunEnvironment
        {
            Input = input,
            Pixels = pixels ?? new FakePixels(),
            Windows = windows ?? new FakeWindows(),
            Space = space
        };
        Assert.True(runner.Start(flow, env, new RunOptions { HighPrecisionTimer = false, SkipStartDelay = true }));
        during?.Invoke(runner);
        Assert.True(runner.Join(TimeSpan.FromMilliseconds(timeoutMs)), "o fluxo não terminou a tempo");
        return (runner, input, info!);
    }
}
