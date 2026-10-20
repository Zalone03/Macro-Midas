using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Views.Overlay;

// Janela de sobreposição sem bordas, sempre no topo, fora da barra de tarefas.
public class OverlayWindow : Window
{
    private PixelRect? _rect;
    private bool _clickThrough = true;

    public bool ExcludeFromCapture { get; init; } = true;
    public bool NoActivate { get; init; } = true;
    public bool KeepTopmost { get; set; } = true;

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            if (IsVisible) ApplyNativeStyles();
        }
    }

    public OverlayWindow()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        ScalingChanged += (_, _) => Reapply();
    }

    public IntPtr Hwnd => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    // Roda uma animação em laço via estilo (RunAsync não aceita laço infinito).
    protected static IDisposable Loop(Animation animation, Control target)
    {
        const string cls = "mmLoop";
        var style = new Style(x => x.Is<Control>().Class(cls)) { Animations = { animation } };
        target.Styles.Add(style);
        target.Classes.Add(cls);
        return new StopLoop(target, style, cls);
    }

    private sealed class StopLoop(Control target, Style style, string cls) : IDisposable
    {
        public void Dispose()
        {
            target.Classes.Remove(cls);
            target.Styles.Remove(style);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ApplyNativeStyles();
        Reapply();
    }

    public void ApplyNativeStyles()
    {
        var h = Hwnd;
        if (h == IntPtr.Zero) return;
        var ex = GetWindowLong(h, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        ex = NoActivate ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
        ex = _clickThrough ? ex | WS_EX_LAYERED | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(h, GWL_EXSTYLE, ex);
        if (_clickThrough) SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
        if (ExcludeFromCapture) SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE);
    }

    // Posiciona em pixels físicos (independe do DPI de cada monitor).
    public void PlaceOn(PixelRect rect)
    {
        _rect = rect;
        if (!IsVisible)
        {
            Position = rect.Position;
            return;
        }
        Reapply();
    }

    private void Reapply()
    {
        var h = Hwnd;
        if (_rect is not { } r || h == IntPtr.Zero) return;
        var insertAfter = KeepTopmost ? HWND_TOPMOST : HWND_TOP;
        var flags = SWP_NOACTIVATE | SWP_SHOWWINDOW | (KeepTopmost ? 0 : SWP_NOZORDER);
        SetWindowPos(h, insertAfter, r.X, r.Y, r.Width, r.Height, flags);
    }

    // Coloca esta janela logo acima de outra na ordem Z (sem ser "sempre no topo").
    public void PlaceAbove(IntPtr other, PixelRect rect)
    {
        _rect = rect;
        var h = Hwnd;
        if (h == IntPtr.Zero) return;
        var prev = GetWindow(other, GW_HWNDPREV);
        var flags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
        IntPtr insertAfter;
        if (prev == h)
        {
            insertAfter = IntPtr.Zero;
            flags |= SWP_NOZORDER;
        }
        else
        {
            insertAfter = prev == IntPtr.Zero ? HWND_TOP : prev;
        }
        SetWindowPos(h, insertAfter, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }
}
