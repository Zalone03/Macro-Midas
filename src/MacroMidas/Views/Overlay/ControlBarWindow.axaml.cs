using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MacroMidas.Models;
using MacroMidas.ViewModels;

namespace MacroMidas.Views.Overlay;

// Barra translúcida (pausar / parar) embaixo da tela.
public partial class ControlBarWindow : OverlayWindow
{
    private bool _movedByUser;

    public ControlBarPlacement Placement { get; set; } = ControlBarPlacement.Bottom;

    public ControlBarWindow()
    {
        ClickThrough = false;
        InitializeComponent();

        Bar.PointerEntered += (_, _) => Bar.Opacity = 1;
        Bar.PointerExited += (_, _) => Bar.Opacity = (DataContext as ControlBarViewModel)?.RestOpacity ?? 0.6;
        Bar.PointerPressed += (_, e) =>
        {
            if (e.Source is not Border && e.Source is not StackPanel && e.Source is not TextBlock) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _movedByUser = true;
            BeginMoveDrag(e);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty) Reposition();
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ControlBarViewModel vm) Bar.Opacity = vm.RestOpacity;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Reposition();
    }

    public void ResetPosition()
    {
        _movedByUser = false;
        Reposition();
    }

    private void Reposition()
    {
        if (_movedByUser) return;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var wa = screen.WorkingArea;
        var scale = screen.Scaling;
        var w = (int)Math.Ceiling(ClientSize.Width * scale);
        var h = (int)Math.Ceiling(ClientSize.Height * scale);
        if (w <= 0 || h <= 0) return;
        var x = wa.X + (wa.Width - w) / 2;
        var y = Placement == ControlBarPlacement.Top ? wa.Y : wa.Bottom - h;
        Position = new PixelPoint(x, y);
    }
}
