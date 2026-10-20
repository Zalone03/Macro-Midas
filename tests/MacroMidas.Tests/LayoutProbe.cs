using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacroMidas.Models;
using MacroMidas.Services;
using MacroMidas.ViewModels;
using MacroMidas.Views;

namespace MacroMidas.Tests;

// Nenhum campo do painel de propriedades fica mais largo que o painel.
public class LayoutProbe
{
    [AvaloniaFact]
    public void Inspector_content_fits_its_panel_for_every_step_type()
    {
        TestData.UseTempRoot();
        var settings = new SettingsService();
        settings.Load();
        var w = new MainWindow { Width = 1280, Height = 820 };
        var vm = new MainWindowViewModel(settings, new FlowLibrary(), w, systemIntegration: false);
        w.DataContext = vm;
        w.Show();
        vm.NewFlowCommand.Execute(null);
        var editor = vm.Editor!;

        var problems = new List<string>();
        foreach (var kind in StepFactory.All.Select(i => i.Kind))
        {
            var step = StepFactory.Create(kind);
            editor.InsertSteps([step]);
            editor.SelectedStep = step;

            ScrollViewer? inspector = null;
            for (var i = 0; i < 40 && inspector == null; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
                w.UpdateLayout();
                inspector = w.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(s => s.IsEffectivelyVisible && s.FindAncestorOfType<Border>() is { Name: "InspectorPanel" });
            }
            Assert.True(inspector != null, $"painel de opções não apareceu para “{kind}”");
            var content = inspector!.GetVisualDescendants().OfType<ScrollContentPresenter>().First().Child as Control;
            Assert.NotNull(content);
            var limit = content!.Bounds.Width + 0.5;
            problems.AddRange(content.GetVisualDescendants().OfType<Control>()
                .Where(c => c.IsVisible && c.Bounds.Width > limit)
                .Select(c => $"{kind}: {c.GetType().Name} {c.Bounds.Width:0}px > {limit:0}px"));
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
    }
}
