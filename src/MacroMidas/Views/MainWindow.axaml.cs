using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using MacroMidas.Models;
using MacroMidas.Services;
using MacroMidas.ViewModels;

namespace MacroMidas.Views;

public partial class MainWindow : Window, IWindowHost
{
    private WindowState _stateBeforeSession = WindowState.Normal;
    private bool _minimizedBySession;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.PointerPressed += OnTitleBarPressed;
        FlowList.Tapped += (_, _) =>
        {
            if (Vm is { IsSettingsOpen: true } vm) vm.IsSettingsOpen = false;
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    Window IWindowHost.Window => this;

    public IStorageProvider? Storage => StorageProvider;

    public void MinimizeForSession()
    {
        if (WindowState == WindowState.Minimized) return;
        _stateBeforeSession = WindowState;
        _minimizedBySession = true;
        WindowState = WindowState.Minimized;
    }

    public void RestoreAfterSession()
    {
        if (!_minimizedBySession) return;
        _minimizedBySession = false;
        WindowState = _stateBeforeSession == WindowState.Minimized ? WindowState.Normal : _stateBeforeSession;
        Activate();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Vm is not { } vm) return;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && vm.IsSettingsOpen)
        {
            vm.IsSettingsOpen = false;
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers != KeyModifiers.Control) return;
        switch (e.Key)
        {
            case Key.N:
                vm.NewFlowCommand.Execute(null);
                break;
            case Key.O:
                vm.ImportCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Vm?.Shutdown();
        base.OnClosing(e);
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnRunOptionsClicked(object? sender, RoutedEventArgs e) =>
        this.GetVisualDescendants().OfType<FlowEditorView>().FirstOrDefault()?.OpenRunSettings();

    private static Flow? FlowOf(object? sender) => (sender as Control)?.DataContext as Flow;

    private void OnRunFlowClicked(object? sender, RoutedEventArgs e)
    {
        if (FlowOf(sender) is { } f) Vm?.StartFlow(f);
    }

    private void OnDuplicateFlowClicked(object? sender, RoutedEventArgs e)
    {
        if (FlowOf(sender) is { } f) Vm?.DuplicateFlowCommand.Execute(f);
    }

    private void OnDeleteFlowClicked(object? sender, RoutedEventArgs e)
    {
        if (FlowOf(sender) is { } f) Vm?.DeleteFlowCommand.Execute(f);
    }

    private void OnExportMmfClicked(object? sender, RoutedEventArgs e) => ExportOf(sender, "mmf");

    private void OnExportTxtClicked(object? sender, RoutedEventArgs e) => ExportOf(sender, "txt");

    private void ExportOf(object? sender, string format)
    {
        if (FlowOf(sender) is not { } f || Vm == null) return;
        Vm.SelectedFlow = f;
        Vm.ExportCommand.Execute(format);
    }
}
