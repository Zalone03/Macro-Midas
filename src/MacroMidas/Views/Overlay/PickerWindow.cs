using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MacroMidas.Capture;
using MacroMidas.Native;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Views.Overlay;

public enum PickMode { Point, Region, Window }

public sealed record PickResult(
    PickMode Mode, int X, int Y, RgbColor Color,
    int Width = 0, int Height = 0,
    IntPtr Window = default, string? WindowTitle = null, string? ProcessName = null);

// Seletor em tela cheia (um por monitor): mira, lupa com os pixels ampliados, coordenadas e cor.
public sealed class PickerWindow : OverlayWindow
{
    private const int MagPixels = 15;
    private const int MagZoom = 8;

    private readonly PickMode _mode;
    private readonly PixelRect _screen;
    private readonly Canvas _canvas = new();
    private readonly Line _lineH, _lineV;
    private readonly Border _magnifier;
    private readonly Image _magImage = new() { Width = MagPixels * MagZoom, Height = MagPixels * MagZoom, Stretch = Stretch.Fill };
    private readonly TextBlock _coords = new() { FontSize = 11.5, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), Foreground = Brushes.White };
    private readonly TextBlock _hex = new() { FontSize = 11.5, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), Foreground = new SolidColorBrush(Color.Parse("#A09EA6")) };
    private readonly Border _swatch = new() { Width = 14, Height = 14, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#66FFFFFF")) };
    private readonly Border _selection;
    private readonly TextBlock _selectionLabel = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#E4E2DE")) };
    private readonly Border _selectionTag;
    private WriteableBitmap? _bitmap;
    private bool _dragging;
    private PixelPoint _dragStart;
    private PixelPoint _cursor;
    private RgbColor _color;
    private WindowInfo? _hoverWindow;
    private bool _done;

    public event Action<PickResult?>? Completed;

    public PickerWindow(PixelRect screen, PickMode mode, string hint)
    {
        _mode = mode;
        _screen = screen;
        ClickThrough = false;
        NoActivate = false;
        ShowActivated = true;
        Title = "Macro Midas: Capturar";
        Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        Cursor = new Cursor(StandardCursorType.Cross);

        var gold = new SolidColorBrush(Color.Parse("#B3C9A75E"));
        var line = new SolidColorBrush(Color.Parse("#26FFFFFF"));
        _lineH = new Line { Stroke = gold, StrokeThickness = 1, StrokeDashArray = [4, 3], Opacity = 0.8, IsHitTestVisible = false };
        _lineV = new Line { Stroke = gold, StrokeThickness = 1, StrokeDashArray = [4, 3], Opacity = 0.8, IsHitTestVisible = false };

        RenderOptions.SetBitmapInterpolationMode(_magImage, BitmapInterpolationMode.None);
        var center = new Border
        {
            Width = MagZoom + 2, Height = MagZoom + 2,
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _magnifier = new Border
        {
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.Parse("#F2141415")),
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #88000000"),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        ClipToBounds = true,
                        Child = new Grid { Children = { _magImage, center } }
                    },
                    _coords,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _swatch, _hex } }
                }
            }
        };

        _selection = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            BorderBrush = gold,
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.Parse("#14C9A75E")),
            CornerRadius = new CornerRadius(4)
        };
        _selectionTag = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.Parse("#E6141415")),
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 2),
            Child = _selectionLabel
        };

        var hintBar = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 28, 0, 0),
            Padding = new Thickness(16, 10),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.Parse("#EB141415")),
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = hint, Foreground = new SolidColorBrush(Color.Parse("#E4E2DE")), FontSize = 13 }
        };

        _canvas.Children.Add(_selection);
        _canvas.Children.Add(_selectionTag);
        _canvas.Children.Add(_lineH);
        _canvas.Children.Add(_lineV);
        _canvas.Children.Add(_magnifier);

        Content = new Grid { Children = { _canvas, hintBar } };
        PlaceOn(screen);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        GetCursorPos(out var p);
        if (_screen.Contains(new PixelPoint(p.X, p.Y)))
        {
            Activate();
            Focus();
        }
        UpdateAt(new PixelPoint(p.X, p.Y));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        GetCursorPos(out var p);
        UpdateAt(new PixelPoint(p.X, p.Y));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _magnifier.IsVisible = _lineH.IsVisible = _lineV.IsVisible = false;
        if (!_dragging) _selection.IsVisible = _selectionTag.IsVisible = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            Finish(null);
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        GetCursorPos(out var p);
        _cursor = new PixelPoint(p.X, p.Y);
        switch (_mode)
        {
            case PickMode.Point:
                Finish(PointResult());
                break;
            case PickMode.Window:
                if (_hoverWindow != null) Finish(WindowResult(_hoverWindow));
                break;
            case PickMode.Region:
                _dragging = true;
                _dragStart = _cursor;
                e.Pointer.Capture(this);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode != PickMode.Region || !_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        GetCursorPos(out var p);
        var r = Normalize(_dragStart, new PixelPoint(p.X, p.Y));
        if (r.Width < 2 || r.Height < 2)
        {
            r = new PixelRect(p.X - 20, p.Y - 20, 40, 40);
        }
        Finish(new PickResult(PickMode.Region, r.X, r.Y, _color, r.Width, r.Height));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        GetCursorPos(out var p);
        switch (e.Key)
        {
            case Key.Escape:
                Finish(null);
                break;
            case Key.Enter or Key.Space:
                _cursor = new PixelPoint(p.X, p.Y);
                if (_mode == PickMode.Window && _hoverWindow != null) Finish(WindowResult(_hoverWindow));
                else if (_mode == PickMode.Point) Finish(PointResult());
                break;
            case Key.Left: SetCursorPos(p.X - step, p.Y); break;
            case Key.Right: SetCursorPos(p.X + step, p.Y); break;
            case Key.Up: SetCursorPos(p.X, p.Y - step); break;
            case Key.Down: SetCursorPos(p.X, p.Y + step); break;
            default: return;
        }
        e.Handled = true;
        GetCursorPos(out p);
        UpdateAt(new PixelPoint(p.X, p.Y));
    }

    private PickResult PointResult() => new(PickMode.Point, _cursor.X, _cursor.Y, _color);

    private static PickResult WindowResult(WindowInfo w) =>
        new(PickMode.Window, 0, 0, default, Window: w.Handle, WindowTitle: w.Title, ProcessName: w.ProcessName);

    private void Finish(PickResult? result)
    {
        if (_done) return;
        _done = true;
        Completed?.Invoke(result);
    }

    private Point ToLocal(PixelPoint screenPoint)
    {
        var s = RenderScaling <= 0 ? 1 : RenderScaling;
        return new Point((screenPoint.X - _screen.X) / s, (screenPoint.Y - _screen.Y) / s);
    }

    private static PixelRect Normalize(PixelPoint a, PixelPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void UpdateAt(PixelPoint p)
    {
        _cursor = p;
        var inside = _screen.Contains(p);
        _magnifier.IsVisible = _lineH.IsVisible = _lineV.IsVisible = inside;
        if (!inside) return;

        var local = ToLocal(p);
        var size = Bounds.Size;
        _lineH.StartPoint = new Point(0, local.Y + 0.5);
        _lineH.EndPoint = new Point(size.Width, local.Y + 0.5);
        _lineV.StartPoint = new Point(local.X + 0.5, 0);
        _lineV.EndPoint = new Point(local.X + 0.5, size.Height);

        CaptureMagnifier(p);

        var magW = MagPixels * MagZoom + 16;
        var magH = MagPixels * MagZoom + 64;
        var mx = local.X + 26 + magW > size.Width ? local.X - 26 - magW : local.X + 26;
        var my = local.Y + 26 + magH > size.Height ? local.Y - 26 - magH : local.Y + 26;
        Canvas.SetLeft(_magnifier, Math.Max(4, mx));
        Canvas.SetTop(_magnifier, Math.Max(4, my));

        switch (_mode)
        {
            case PickMode.Region when _dragging:
                ShowSelection(Normalize(_dragStart, p), $"{Math.Abs(p.X - _dragStart.X)} × {Math.Abs(p.Y - _dragStart.Y)}");
                break;
            case PickMode.Window:
                _hoverWindow = WindowFinder.TopLevelAt(p.X, p.Y);
                if (_hoverWindow != null && WindowFinder.GetVisibleBounds(_hoverWindow.Handle) is { } b)
                    ShowSelection(new PixelRect(b.Left, b.Top, b.Width, b.Height), _hoverWindow.Title);
                else
                    _selection.IsVisible = _selectionTag.IsVisible = false;
                break;
        }
    }

    private void ShowSelection(PixelRect r, string label)
    {
        var tl = ToLocal(r.Position);
        var s = RenderScaling <= 0 ? 1 : RenderScaling;
        Canvas.SetLeft(_selection, tl.X);
        Canvas.SetTop(_selection, tl.Y);
        _selection.Width = Math.Max(1, r.Width / s);
        _selection.Height = Math.Max(1, r.Height / s);
        _selection.IsVisible = true;
        _selectionLabel.Text = label.Length > 60 ? label[..60] + "…" : label;
        Canvas.SetLeft(_selectionTag, tl.X);
        Canvas.SetTop(_selectionTag, Math.Max(0, tl.Y - 24));
        _selectionTag.IsVisible = true;
    }

    private void CaptureMagnifier(PixelPoint p)
    {
        var half = MagPixels / 2;
        var img = GdiCapture.CaptureScreen(p.X - half, p.Y - half, MagPixels, MagPixels);
        if (img == null) return;

        _color = img.GetPixel(half, half);
        _bitmap ??= new WriteableBitmap(new PixelSize(MagPixels, MagPixels), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var px = img.Pixels;
        for (var i = 3; i < px.Length; i += 4) px[i] = 255;
        using (var fb = _bitmap.Lock())
        {
            for (var y = 0; y < MagPixels; y++)
                Marshal.Copy(px, y * MagPixels * 4, fb.Address + y * fb.RowBytes, MagPixels * 4);
        }
        _magImage.Source = null;
        _magImage.Source = _bitmap;

        _coords.Text = $"X {p.X}   Y {p.Y}";
        _hex.Text = _color.ToHex();
        _swatch.Background = new SolidColorBrush(Color.FromRgb(_color.R, _color.G, _color.B));
    }
}
