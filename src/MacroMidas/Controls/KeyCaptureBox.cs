using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacroMidas.Input;

namespace MacroMidas.Controls;

// Campo da tecla de um passo: clique nele e aperte a tecla ou a combinação ("Ctrl+Shift+S").
public class KeyCaptureBox : TextBox
{
    public static readonly StyledProperty<bool> IsCapturingProperty =
        AvaloniaProperty.Register<KeyCaptureBox, bool>(nameof(IsCapturing));

    private string? _pendingModifier;
    private string? _before;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public bool IsCapturing
    {
        get => GetValue(IsCapturingProperty);
        set => SetValue(IsCapturingProperty, value);
    }

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Clique e aperte a tecla");
        AddHandler(PointerPressedEvent, (_, _) => StartCapture(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void StartCapture()
    {
        if (IsCapturing) return;
        _before = Text;
        _pendingModifier = null;
        IsCapturing = true;
        Classes.Set("capturing", true);
        Text = "";
        Watermark = "Aperte a tecla… (Esc cancela)";
        Focus();
        KeyCapture.Begin();
    }

    private void StopCapture(bool restore)
    {
        if (!IsCapturing) return;
        IsCapturing = false;
        Classes.Set("capturing", false);
        if (restore) Text = _before;
        Watermark = null;
        _pendingModifier = null;
        KeyCapture.End();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                StartCapture();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            StopCapture(restore: true);
            return;
        }

        var chord = VirtualKeys.ChordFromAvalonia(e.Key, e.KeyModifiers);
        if (chord == null) return;

        if (KeyCapture.IsModifierKey(e.Key))
        {
            _pendingModifier = chord;
            Text = KeyCapture.ModifierPreview(e.KeyModifiers) + "…";
            return;
        }

        Text = chord;
        StopCapture(restore: false);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (IsCapturing && _pendingModifier != null && KeyCapture.IsModifierKey(e.Key))
        {
            Text = _pendingModifier;
            StopCapture(restore: false);
            e.Handled = true;
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        e.Handled = true;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        if (IsCapturing) StopCapture(restore: true);
        base.OnLostFocus(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (IsCapturing) StopCapture(restore: true);
        base.OnDetachedFromVisualTree(e);
    }
}
