using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MacroMidas.ViewModels;

namespace MacroMidas.Views;

// Posição na tela de forma simples: botão "Escolher na tela" + as coordenadas em texto.
public class PointField : UserControl
{
    public static readonly StyledProperty<int> XProperty =
        AvaloniaProperty.Register<PointField, int>(nameof(X), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> YProperty =
        AvaloniaProperty.Register<PointField, int>(nameof(Y), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PointField, string>(nameof(Label), "");

    // Parâmetro enviado ao PickCommand do editor ("Point", "From", "To", "Pixel"...).
    public static readonly StyledProperty<string> PickTargetProperty =
        AvaloniaProperty.Register<PointField, string>(nameof(PickTarget), "Point");

    public static readonly StyledProperty<string> PickTextProperty =
        AvaloniaProperty.Register<PointField, string>(nameof(PickText), "Escolher na tela");

    public int X { get => GetValue(XProperty); set => SetValue(XProperty, value); }
    public int Y { get => GetValue(YProperty); set => SetValue(YProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string PickTarget { get => GetValue(PickTargetProperty); set => SetValue(PickTargetProperty, value); }
    public string PickText { get => GetValue(PickTextProperty); set => SetValue(PickTextProperty, value); }

    private readonly TextBlock _coords = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Margin = new Thickness(12, 0, 0, 0) };

    public PointField()
    {
        var label = new TextBlock { Classes = { "label" } };
        label.Bind(TextBlock.TextProperty, new Binding(nameof(Label)) { Source = this });
        label.Bind(IsVisibleProperty, new Binding(nameof(Label)) { Source = this, Converter = StringConverters.IsNotNullOrEmpty });

        var pickText = new TextBlock();
        pickText.Bind(TextBlock.TextProperty, new Binding(nameof(PickText)) { Source = this });
        var pick = new Button
        {
            Classes = { "outline" },
            Padding = new Thickness(10, 6),
            Theme = Res<ControlTheme>("GhostButton"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { new Controls.Icon { Width = 14, Height = 14, Data = Res<Geometry>("Icon.Crosshair") }, pickText }
            }
        };
        ToolTip.SetTip(pick, "Clique e depois aponte o lugar na tela (a lupa ajuda a mirar)");
        pick.Click += async (_, _) =>
        {
            if (this.FindAncestorOfType<FlowEditorView>()?.DataContext is FlowEditorViewModel vm)
                await vm.PickCommand.ExecuteAsync(PickTarget);
        };

        _coords.Foreground = Res<IBrush>("Text2Brush");

        var edit = new ToggleButton
        {
            Theme = Res<ControlTheme>("IconToggle"),
            Width = 26,
            Height = 26,
            Content = new Controls.Icon { Width = 12, Height = 12, Data = Res<Geometry>("Icon.Edit") }
        };
        ToolTip.SetTip(edit, "Ajustar os números à mão");

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(_coords, 1);
        Grid.SetColumn(edit, 2);
        row.Children.Add(pick);
        row.Children.Add(_coords);
        row.Children.Add(edit);

        var x = Number("X");
        var y = Number("Y");
        x.Bind(NumericUpDown.ValueProperty, new Binding(nameof(X)) { Source = this, Mode = BindingMode.TwoWay });
        y.Bind(NumericUpDown.ValueProperty, new Binding(nameof(Y)) { Source = this, Mode = BindingMode.TwoWay });
        var numbers = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetColumn(y, 2);
        numbers.Children.Add(x);
        numbers.Children.Add(y);
        numbers.Bind(IsVisibleProperty, new Binding(nameof(ToggleButton.IsChecked)) { Source = edit });

        Content = new StackPanel { Children = { label, row, numbers } };
        UpdateCoords();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == XProperty || change.Property == YProperty) UpdateCoords();
    }

    private void UpdateCoords() => _coords.Text = $"{X}, {Y}";

    private static NumericUpDown Number(string axis) => new()
    {
        Minimum = -100000,
        Maximum = 100000,
        Increment = 1,
        FormatString = "0",
        InnerLeftContent = new TextBlock
        {
            Text = axis,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res<IBrush>("Text3Brush"),
            FontSize = 11
        }
    };

    private static T? Res<T>(string key) where T : class =>
        Application.Current?.TryFindResource(key, out var v) == true ? v as T : null;
}
