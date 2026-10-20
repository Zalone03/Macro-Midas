using Avalonia.Controls;
using MacroMidas.Views.Overlay;

namespace MacroMidas.Services;

// Esconde o app, mostra o seletor em todos os monitores e devolve o que o usuário escolheu.
public sealed class PickerService(Window owner)
{
    public bool IsPicking { get; private set; }

    public async Task<PickResult?> PickAsync(PickMode mode, string? hint = null)
    {
        if (IsPicking) return null;
        IsPicking = true;
        var previousState = owner.WindowState;
        var windows = new List<PickerWindow>();
        try
        {
            owner.WindowState = WindowState.Minimized;
            await Task.Delay(260);

            hint ??= mode switch
            {
                PickMode.Region => "Arraste para selecionar a área  ·  Esc cancela",
                PickMode.Window => "Clique na janela que o fluxo vai controlar  ·  Esc cancela",
                _ => "Clique no ponto  ·  setas ajustam 1 px (Shift = 10)  ·  Enter confirma  ·  Esc cancela"
            };

            var tcs = new TaskCompletionSource<PickResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var screen in owner.Screens.All)
            {
                var w = new PickerWindow(screen.Bounds, mode, hint);
                w.Completed += r => tcs.TrySetResult(r);
                w.Closed += (_, _) => tcs.TrySetResult(null);
                windows.Add(w);
                w.Show();
            }
            return await tcs.Task;
        }
        finally
        {
            foreach (var w in windows) w.Close();
            owner.WindowState = previousState == WindowState.Minimized ? WindowState.Normal : previousState;
            owner.Activate();
            IsPicking = false;
        }
    }
}
