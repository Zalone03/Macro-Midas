using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using MacroMidas.Input;

namespace MacroMidas.Controls;

// Avisa quando uma tecla está sendo capturada (os atalhos globais ficam desligados).
public static class KeyCapture
{
    private static int _active;

    public static event Action<bool>? StateChanged;

    public static bool IsActive => _active > 0;

    internal static void Begin()
    {
        if (++_active == 1) StateChanged?.Invoke(true);
    }

    internal static void End()
    {
        if (_active == 0) return;
        if (--_active == 0) StateChanged?.Invoke(false);
    }

    internal static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    internal static string ModifierPreview(KeyModifiers m)
    {
        var parts = new List<string>();
        if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        return parts.Count == 0 ? "" : string.Join("+", parts) + "+";
    }
}

// Teclas de atalho como botões: clique para trocar, "+ Adicionar" para outra, × para tirar.
public class HotkeyEditor : UserControl
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<HotkeyEditor, string?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> AllowMultipleProperty =
        AvaloniaProperty.Register<HotkeyEditor, bool>(nameof(AllowMultiple), true);

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<HotkeyEditor, string>(nameof(EmptyText), "Definir tecla");

    private readonly WrapPanel _panel = new() { Orientation = Orientation.Horizontal };
    private int _capture = -1;
    private string? _pendingModifier;
    private TextBlock? _captureText;

    public HotkeyEditor()
    {
        Focusable = true;
        Content = _panel;
        Rebuild();
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool AllowMultiple
    {
        get => GetValue(AllowMultipleProperty);
        set => SetValue(AllowMultipleProperty, value);
    }

    public string EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public bool IsCapturing => _capture >= 0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == AllowMultipleProperty || change.Property == EmptyTextProperty)
            Rebuild();
    }

    private List<string> Chords() => [.. HotkeyList.Split(Value)];

    // Começa a capturar: troca a tecla do índice ou adiciona uma nova (índice = quantidade).
    public void StartCapture(int index)
    {
        if (_capture < 0) KeyCapture.Begin();
        _capture = Math.Clamp(index, 0, Chords().Count);
        _pendingModifier = null;
        Rebuild();
        Focus();
    }

    public void CancelCapture()
    {
        if (_capture < 0) return;
        _capture = -1;
        _pendingModifier = null;
        KeyCapture.End();
        Rebuild();
    }

    private void Commit(string chord)
    {
        var list = Chords();
        var normalized = VirtualKeys.Normalize(chord) ?? chord;
        if (_capture >= 0 && _capture < list.Count) list[_capture] = normalized;
        else list.Add(normalized);
        _capture = -1;
        _pendingModifier = null;
        KeyCapture.End();
        Value = HotkeyList.Join(AllowMultiple ? list : list.Take(1));
        Rebuild();
    }

    private void Remove(int index)
    {
        var list = Chords();
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        Value = HotkeyList.Join(list);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!IsCapturing)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            CancelCapture();
            return;
        }
        var chord = VirtualKeys.ChordFromAvalonia(e.Key, e.KeyModifiers);
        if (chord == null) return;
        if (KeyCapture.IsModifierKey(e.Key))
        {
            _pendingModifier = chord;
            if (_captureText != null) _captureText.Text = KeyCapture.ModifierPreview(e.KeyModifiers) + "…";
            return;
        }
        Commit(chord);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (IsCapturing && _pendingModifier != null && KeyCapture.IsModifierKey(e.Key))
        {
            e.Handled = true;
            Commit(_pendingModifier);
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        CancelCapture();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelCapture();
        base.OnDetachedFromVisualTree(e);
    }

    private void Rebuild()
    {
        _panel.Children.Clear();
        _captureText = null;
        var chords = Chords();
        for (var i = 0; i < chords.Count; i++)
        {
            var index = i;
            _panel.Children.Add(index == _capture ? CaptureChip() : Chip(chords[i], index));
        }
        if (_capture >= 0 && _capture >= chords.Count) _panel.Children.Add(CaptureChip());
        else if (AllowMultiple || chords.Count == 0)
            _panel.Children.Add(AddButton(chords.Count == 0 ? EmptyText : "+ Adicionar", chords.Count));
    }

    private static T? Res<T>(string key) where T : class =>
        Application.Current?.TryFindResource(key, out var v) == true ? v as T : null;

    private Control Chip(string chord, int index)
    {
        var change = new Button
        {
            Theme = Res<ControlTheme>("GhostButton"),
            Focusable = false,
            Padding = new Thickness(10, 4, 6, 4),
            Content = new TextBlock { Text = chord, FontSize = 12.5, FontWeight = FontWeight.SemiBold },
        };
        ToolTip.SetTip(change, "Clique e aperte outra tecla para trocar");
        change.Click += (_, _) => StartCapture(index);

        var remove = new Button
        {
            Theme = Res<ControlTheme>("IconButton"),
            Focusable = false,
            Width = 22,
            Height = 22,
            Margin = new Thickness(0, 0, 3, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Content = new Icon { Width = 9, Height = 9, Data = Res<Geometry>("Icon.Close") },
        };
        ToolTip.SetTip(remove, "Tirar este atalho");
        remove.Click += (_, _) => Remove(index);

        return Frame(new StackPanel { Orientation = Orientation.Horizontal, Children = { change, remove } }, active: false);
    }

    private Control CaptureChip()
    {
        _captureText = new TextBlock
        {
            Text = _pendingModifier ?? "Aperte a tecla… (Esc cancela)",
            FontSize = 12.5,
            Margin = new Thickness(10, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return Frame(_captureText, active: true);
    }

    private Control AddButton(string text, int index)
    {
        var add = new Button
        {
            Theme = Res<ControlTheme>("GhostButton"),
            Focusable = false,
            Padding = new Thickness(10, 5),
            Margin = new Thickness(0, 0, 6, 6),
            Content = new TextBlock { Text = text, FontSize = 12.5 },
        };
        add.Classes.Add("outline");
        ToolTip.SetTip(add, "Clique e aperte a tecla que quer usar");
        add.Click += (_, _) => StartCapture(index);
        return add;
    }

    private static Border Frame(Control child, bool active) => new()
    {
        Child = child,
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 0, 6, 6),
        Background = new SolidColorBrush(active ? Color.Parse("#24C9A75E") : Color.Parse("#161618")),
        BorderBrush = new SolidColorBrush(active ? Color.Parse("#80C9A75E") : Color.Parse("#2C2C2F")),
    };
}
