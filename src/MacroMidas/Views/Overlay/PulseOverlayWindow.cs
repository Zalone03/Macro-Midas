using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace MacroMidas.Views.Overlay;

// Borda dourada pulsando em volta de um monitor inteiro enquanto o fluxo roda.
public sealed class PulseOverlayWindow : OverlayWindow
{
    private readonly Grid _root = new() { IsHitTestVisible = false };
    private readonly Grid _pulse = new();
    private IDisposable? _anim;

    public PulseOverlayWindow()
    {
        Title = "Macro Midas: Overlay";
        _root.Children.Add(_pulse);
        Content = _root;
    }

    public void Configure(Color color, double opacity, int thickness, int glow)
    {
        _root.Opacity = Math.Clamp(opacity, 0.05, 1);
        _pulse.Children.Clear();

        var strong = Color.FromArgb(220, color.R, color.G, color.B);
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        glow = Math.Max(0, glow);

        if (glow > 0)
        {
            _pulse.Children.Add(Edge(VerticalAlignment.Top, HorizontalAlignment.Stretch, glow, double.NaN, Gradient(strong, clear, vertical: true)));
            _pulse.Children.Add(Edge(VerticalAlignment.Bottom, HorizontalAlignment.Stretch, glow, double.NaN, Gradient(clear, strong, vertical: true)));
            _pulse.Children.Add(Edge(VerticalAlignment.Stretch, HorizontalAlignment.Left, double.NaN, glow, Gradient(strong, clear, vertical: false)));
            _pulse.Children.Add(Edge(VerticalAlignment.Stretch, HorizontalAlignment.Right, double.NaN, glow, Gradient(clear, strong, vertical: false)));
        }

        if (thickness > 0)
        {
            var light = Blend(color, Colors.White, 0.55);
            var deep = Blend(color, Colors.Black, 0.25);
            _pulse.Children.Add(new Border
            {
                BorderThickness = new Thickness(thickness),
                BorderBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(light, 0),
                        new GradientStop(color, 0.45),
                        new GradientStop(deep, 1),
                    }
                }
            });
        }
    }

    public void StartPulse(double seconds)
    {
        StopPulse();
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.3, 10) / 2),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.28) } },
            }
        };
        _anim = Loop(animation, _pulse);
    }

    public void StopPulse()
    {
        _anim?.Dispose();
        _anim = null;
        _pulse.Opacity = 1;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPulse();
        base.OnClosed(e);
    }

    private static Border Edge(VerticalAlignment v, HorizontalAlignment h, double height, double width, IBrush brush) => new()
    {
        VerticalAlignment = v,
        HorizontalAlignment = h,
        Height = height,
        Width = width,
        Background = brush
    };

    private static LinearGradientBrush Gradient(Color from, Color to, bool vertical) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = vertical ? new RelativePoint(0, 1, RelativeUnit.Relative) : new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
    };

    internal static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
