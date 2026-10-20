using MacroMidas.Models;

namespace MacroMidas.Services;

// Fluxos criados pelo app: um vazio e o Auto-clique pronto para usar (não há outros modelos).
public static class FlowTemplates
{
    public static Flow Empty() => new() { Name = "Novo fluxo" };

    // Clica sem parar onde o mouse estiver, com o mouse de verdade (funciona no Roblox).
    public static Flow AutoClick()
    {
        var f = new Flow { Name = "Auto-clique" };
        f.Settings.RepeatMode = RepeatMode.Infinite;
        f.Settings.StartDelayMs = 1500;
        f.Settings.InputMode = InputMode.Direct;
        f.Steps.Add(new ClickStep { UsePosition = false });
        f.Steps.Add(new WaitStep { Ms = 100 });
        return f;
    }
}
