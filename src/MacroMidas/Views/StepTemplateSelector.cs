using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MacroMidas.Models;

namespace MacroMidas.Views;

// Escolhe o template "{Prefix}.{NomeDoTipo}" pelo tipo do passo, ou "{Prefix}.Default".
public sealed class StepTemplateSelector : IDataTemplate
{
    public string Prefix { get; set; } = "Card";

    public Control? Build(object? param)
    {
        if (param == null || Application.Current is not { } app) return null;
        if (!app.TryFindResource($"{Prefix}.{param.GetType().Name}", out var res))
            app.TryFindResource($"{Prefix}.Default", out res);
        return (res as IDataTemplate)?.Build(param);
    }

    public bool Match(object? data) => data is FlowStep;
}
