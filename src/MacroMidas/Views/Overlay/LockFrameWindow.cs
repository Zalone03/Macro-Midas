using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using MacroMidas.Controls;

namespace MacroMidas.Views.Overlay;

// "Lock": moldura dourada sobre a janela alvo.
public sealed class LockFrameWindow : OverlayWindow
{
    private readonly Grid _root = new();
    private readonly Border _frame;
    private readonly TextBlock _subtitle;
    private IDisposable? _anim;

    public LockFrameWindow()
    {
        Title = "Macro Midas: Lock";
        Topmost = false;
        KeepTopmost = false;

        _frame = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
        };

        _subtitle = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#8A8990")),
            Text = "seu mouse não interfere aqui"
        };

        var badge = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.Parse("#E6141415")),
            BorderBrush = new SolidColorBrush(Color.Parse("#26FFFFFF")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Icon
                    {
                        Width = 13, Height = 13,
                        Foreground = new SolidColorBrush(Color.Parse("#C9A75E")),
                        Data = Geometry.Parse("M6 11h12a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M8 11V7a4 4 0 0 1 8 0v4"),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Em uso pelo Macro Midas",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.Parse("#E4E2DE")),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    _subtitle
                }
            }
        };

        _root.Children.Add(_frame);
        _root.Children.Add(badge);
        Content = _root;
    }

    public void Configure(Color color, bool blocksMouse)
    {
        _frame.BorderBrush = new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B));

        _root.Background = blocksMouse ? new SolidColorBrush(Color.FromArgb(10, 0, 0, 0)) : Brushes.Transparent;
        _root.Cursor = blocksMouse ? new Cursor(StandardCursorType.No) : Cursor.Default;
        _subtitle.Text = blocksMouse ? "seu mouse não interfere aqui" : "rodando em segundo plano";
        ClickThrough = !blocksMouse;
    }

    public void StartPulse(double seconds)
    {
        _anim?.Dispose();
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.3, 10) / 2),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.45) } },
            }
        };
        _anim = Loop(animation, _frame);
    }

    protected override void OnClosed(EventArgs e)
    {
        _anim?.Dispose();
        base.OnClosed(e);
    }
}
