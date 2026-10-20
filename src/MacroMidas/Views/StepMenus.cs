using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MacroMidas.Converters;
using MacroMidas.Models;
using MacroMidas.ViewModels;

namespace MacroMidas.Views;

internal static class MenuRes
{
    public static T? Get<T>(string key) where T : class =>
        Application.Current?.TryFindResource(key, out var v) == true ? v as T : null;

    public static Geometry? IconOf(string key) =>
        IconKeyConverter.Instance.Convert(key, typeof(Geometry), null, CultureInfo.CurrentCulture) as Geometry;

    public static IBrush? BrushOf(StepCategory c) =>
        CategoryBrushConverter.Instance.Convert(c, typeof(IBrush), null, CultureInfo.CurrentCulture) as IBrush;

    public static FlowEditorViewModel? EditorOf(Visual v) => v.FindAncestorOfType<FlowEditorView>()?.DataContext as FlowEditorViewModel;

    public static MenuItem Item(string header, string icon, string? gesture, Action action, bool danger = false, bool enabled = true)
    {
        var mi = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            Icon = new Controls.Icon
            {
                Width = 14, Height = 14, Data = IconOf(icon),
                Foreground = Get<IBrush>(danger ? "DangerBrush" : "Text3Brush")
            },
            InputGesture = gesture != null ? KeyGesture.Parse(gesture) : null
        };
        mi.Click += (_, _) => action();
        return mi;
    }
}

// "+ Adicionar passo": painel com os tipos de passo agrupados.
public class AddStepButton : Button
{
    public static readonly StyledProperty<IList<FlowStep>?> TargetListProperty =
        AvaloniaProperty.Register<AddStepButton, IList<FlowStep>?>(nameof(TargetList));

    // Inserir no fim da lista alvo (true) ou depois da seleção (false).
    public static readonly StyledProperty<bool> AppendProperty =
        AvaloniaProperty.Register<AddStepButton, bool>(nameof(Append), true);

    protected override Type StyleKeyOverride => typeof(Button);

    public IList<FlowStep>? TargetList
    {
        get => GetValue(TargetListProperty);
        set => SetValue(TargetListProperty, value);
    }

    public bool Append
    {
        get => GetValue(AppendProperty);
        set => SetValue(AppendProperty, value);
    }

    // Menu de opções aberto por este botão (exposto para os testes).
    internal Flyout? OpenedFlyout { get; private set; }

    protected override void OnClick()
    {
        base.OnClick();
        if (MenuRes.EditorOf(this) is not { } editor) return;

        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Transient };
        OpenedFlyout = flyout;
        var panel = new StackPanel { Width = 300, Spacing = 1, Margin = new Thickness(-6) };
        foreach (var group in StepFactory.Groups)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Name.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 1,
                Foreground = MenuRes.Get<IBrush>("Text3Brush"),
                Margin = new Thickness(10, panel.Children.Count == 0 ? 6 : 10, 0, 3)
            });
            foreach (var item in group.Items) panel.Children.Add(Option(item, editor, flyout));
        }
        flyout.Content = panel;
        flyout.ShowAt(this);
    }

    private Button Option(PaletteItem item, FlowEditorViewModel editor, Flyout flyout)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 6),
            Theme = MenuRes.Get<ControlTheme>("GhostButton"),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Controls.Icon
                    {
                        Width = 16, Height = 16, Margin = new Thickness(0, 1, 12, 0),
                        VerticalAlignment = VerticalAlignment.Top,
                        Data = MenuRes.IconOf(item.Icon),
                        Foreground = MenuRes.BrushOf(item.Category)
                    },
                    new StackPanel
                    {
                        [Grid.ColumnProperty] = 1,
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = item.Label, FontSize = 13, Foreground = MenuRes.Get<IBrush>("Text1Brush") },
                            new TextBlock { Text = item.Tip, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = MenuRes.Get<IBrush>("Text3Brush") }
                        }
                    }
                }
            }
        };
        var kind = item.Kind;
        button.Click += (_, _) =>
        {
            flyout.Hide();
            if (TargetList is { } list && Append) editor.AddInto(list, kind);
            else editor.AddCommand.Execute(kind);
        };
        return button;
    }
}

// Menu "⋯" de um passo: duplicar, ativar/desativar, mover, copiar, excluir.
public class StepMenuButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        base.OnClick();
        if (DataContext is not FlowStep step || MenuRes.EditorOf(this) is not { } vm) return;

        MenuItem Item(string header, string icon, string? gesture, Action action, bool danger = false) =>
            MenuRes.Item(header, icon, gesture, action, danger);

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Items.Add(Item("Duplicar", "Copy", "Ctrl+D", () => vm.DuplicateCommand.Execute(step)));
        flyout.Items.Add(Item(step.Enabled ? "Desativar" : "Ativar", step.Enabled ? "EyeOff" : "Eye", "Space", () => vm.ToggleEnabledCommand.Execute(step)));
        flyout.Items.Add(Item("Mover para cima", "ChevronUp", "Ctrl+Up", () => vm.MoveUpCommand.Execute(step)));
        flyout.Items.Add(Item("Mover para baixo", "ChevronDown", "Ctrl+Down", () => vm.MoveDownCommand.Execute(step)));
        flyout.Items.Add(Item("Copiar", "Paste", "Ctrl+C", () => vm.CopyCommand.Execute(step)));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("Excluir", "Trash", "Delete", () => vm.DeleteCommand.Execute(step), danger: true));
        flyout.ShowAt(this);
    }
}
