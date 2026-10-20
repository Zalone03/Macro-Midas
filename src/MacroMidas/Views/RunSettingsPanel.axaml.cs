using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using MacroMidas.Native;
using MacroMidas.ViewModels;

namespace MacroMidas.Views;

// Opções de execução do fluxo (abre a partir da linha de resumo do cabeçalho).
public partial class RunSettingsPanel : UserControl
{
    public RunSettingsPanel() => InitializeComponent();

    private FlowEditorViewModel? Vm => DataContext as FlowEditorViewModel;

    // Fecha o painel flutuante que contém este controle.
    public event Action? CloseRequested;

    private void OnWindowChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not WindowInfo w || Vm == null) return;
        Vm.ChooseWindowCommand.Execute(w);
        lb.SelectedItem = null;
        ListToggle.IsChecked = false;
    }

    private async void OnPickWindowClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        CloseRequested?.Invoke();
        await Vm.PickTargetWindowCommand.ExecuteAsync(null);
    }
}
