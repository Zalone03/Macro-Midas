using MacroMidas.Capture;
using MacroMidas.Engine;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Native;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Services;

public sealed record EnvironmentResult(RunEnvironment? Environment, string? Error, string? Warning);

// Monta o "mouse e teclado" e o leitor de pixels de acordo com o alvo e o modo do fluxo.
public static class RunEnvironmentFactory
{
    // Converte 0 a 1000 em pixels, pela tela principal ou pela área da janela alvo.
    public static CoordinateSpace SpaceFor(Flow flow, IntPtr window) =>
        flow.Settings.IsProportional ? ProportionalSpace(window) : CoordinateSpace.Identity;

    // O retângulo de referência do modo proporcional (sempre), para captura e conversão.
    public static CoordinateSpace ProportionalSpace(IntPtr window)
    {
        if (window != IntPtr.Zero && GetClientRect(window, out var rc) && rc.Width > 0 && rc.Height > 0)
            return CoordinateSpace.Proportional(0, 0, rc.Width, rc.Height);
        var (w, h) = PrimaryScreenSize();
        return CoordinateSpace.Proportional(0, 0, w, h);
    }

    public static EnvironmentResult Create(Flow flow)
    {
        var s = flow.Settings;
        if (s.Target != TargetKind.Window)
        {
            return new EnvironmentResult(new RunEnvironment
            {
                Input = s.InputMode == InputMode.Background ? new ScreenMessageBackend() : new DirectInputBackend(IntPtr.Zero),
                Pixels = new ScreenPixelReader(IntPtr.Zero),
                TargetDescription = s.InputMode == InputMode.Background ? "tela inteira (Lock)" : "tela inteira",
                Space = SpaceFor(flow, IntPtr.Zero)
            }, null, null);
        }

        var w = WindowFinder.Find(s.WindowTitle, s.ProcessName);
        if (w == null)
        {
            var what = string.Join(" / ", new[] { s.WindowTitle, s.ProcessName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return new EnvironmentResult(null, string.IsNullOrEmpty(what)
                ? "Escolha a janela do fluxo (clique na frase embaixo do nome do fluxo)."
                : $"Janela não encontrada: {what}. Ela está aberta?", null);
        }

        string? warning = null;
        if (!Elevation.IsAdministrator && Elevation.IsProcessElevated(w.ProcessId))
            warning = $"“{w.Title}” roda como administrador: o Windows bloqueia cliques simulados. " +
                      "Use Opções → Configurações → Abrir como administrador.";

        if (s.InputMode == InputMode.Background)
        {
            if (IsIconic(w.Handle)) ShowWindow(w.Handle, SW_SHOWNOACTIVATE);
            return new EnvironmentResult(new RunEnvironment
            {
                Input = new WindowMessageBackend(w.Handle),
                Pixels = new WindowPixelReader(w.Handle),
                TargetWindow = w.Handle,
                TargetDescription = $"“{w.Title}” (Lock)",
                Space = SpaceFor(flow, w.Handle)
            }, null, warning);
        }

        if (IsIconic(w.Handle)) ShowWindow(w.Handle, SW_RESTORE);
        SetForegroundWindow(w.Handle);
        return new EnvironmentResult(new RunEnvironment
        {
            Input = new DirectInputBackend(w.Handle),
            Pixels = new ScreenPixelReader(w.Handle),
            TargetWindow = w.Handle,
            TargetDescription = $"“{w.Title}”",
            Space = SpaceFor(flow, w.Handle)
        }, null, warning);
    }
}
