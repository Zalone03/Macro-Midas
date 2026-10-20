using CommunityToolkit.Mvvm.Input;
using MacroMidas.Capture;
using MacroMidas.Engine;
using MacroMidas.Models;
using MacroMidas.Native;
using MacroMidas.Views.Overlay;
using static MacroMidas.Native.Win32;

namespace MacroMidas.ViewModels;

public partial class FlowEditorViewModel
{
    // Captura na tela.
    [RelayCommand]
    private Task Pick(string target) => PickFor(target, null);

    private async Task PickFor(string target, string? hint)
    {
        var step = SelectedStep;
        if (step == null || _host.Picker == null) return;

        var mode = target switch
        {
            "Region" => PickMode.Region,
            "CondWindow" => PickMode.Window,
            _ => PickMode.Point
        };
        var r = await _host.Picker.PickAsync(mode, hint);
        if (r == null) return;

        if (mode == PickMode.Window)
        {
            if (step is ConditionalStepBase cw) Change(() => cw.Condition.WindowTitle = r.WindowTitle ?? "");
            return;
        }

        if (!TryToFlowCoords(r.X, r.Y, out var x, out var y)) return;
        Change(() => Apply(step, target, r, x, y));
        _host.Toast(target == "Color" ? $"Cor {r.Color.ToHex()} capturada" : $"Capturado ({x}, {y})" +
            (target == "Pixel" ? $" · {r.Color.ToHex()}" : ""), ToastKind.Success);
    }

    private static void Apply(FlowStep step, string target, PickResult r, int x, int y)
    {
        switch (step)
        {
            case MoveStep m:
                m.Relative = false;
                m.X = x;
                m.Y = y;
                break;
            case ClickStep c:
                c.UsePosition = true;
                c.X = x;
                c.Y = y;
                break;
            case MouseButtonStep b:
                b.UsePosition = true;
                b.X = x;
                b.Y = y;
                break;
            case ScrollStep s:
                s.UsePosition = true;
                s.X = x;
                s.Y = y;
                break;
            case DragStep d when target == "To":
                d.ToX = x;
                d.ToY = y;
                break;
            case DragStep d:
                d.FromX = x;
                d.FromY = y;
                break;
            case ConditionalStepBase cs:
                var cond = cs.Condition;
                switch (target)
                {
                    case "Color":
                        cond.Color = r.Color.ToHex();
                        break;
                    case "Region":
                        cond.Kind = ConditionKind.ColorInRegion;
                        cond.X = x;
                        cond.Y = y;
                        cond.Width = r.Width;
                        cond.Height = r.Height;
                        break;
                    default:
                        if (cond.Kind is not (ConditionKind.PixelColor or ConditionKind.ColorInRegion))
                            cond.Kind = ConditionKind.PixelColor;
                        cond.X = x;
                        cond.Y = y;
                        cond.Color = r.Color.ToHex();
                        break;
                }
                break;
        }
    }

    // Converte coordenadas de tela para as do fluxo (janela alvo e modo proporcional).
    private bool TryToFlowCoords(int sx, int sy, out int x, out int y)
    {
        x = sx;
        y = sy;
        var window = IntPtr.Zero;
        if (RunSettings.Target == TargetKind.Window)
        {
            var w = WindowFinder.Find(RunSettings.WindowTitle, RunSettings.ProcessName);
            if (w == null)
            {
                _host.Toast("Abra a janela alvo antes de capturar (as posições são relativas a ela).", ToastKind.Warning);
                return false;
            }
            window = w.Handle;
            var p = new POINT(sx, sy);
            ScreenToClient(window, ref p);
            x = p.X;
            y = p.Y;
        }
        (x, y) = Services.RunEnvironmentFactory.SpaceFor(Flow, window).ToFlow(x, y);
        return true;
    }

    [RelayCommand]
    private async Task PickTargetWindow()
    {
        if (_host.Picker == null) return;
        var r = await _host.Picker.PickAsync(PickMode.Window);
        if (r?.WindowTitle == null) return;
        Change(() =>
        {
            RunSettings.Target = TargetKind.Window;
            RunSettings.WindowTitle = r.WindowTitle;
            RunSettings.ProcessName = r.ProcessName ?? "";
        });
        _host.Toast($"Alvo: {r.WindowTitle}", ToastKind.Success);
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        Windows.Clear();
        foreach (var w in WindowFinder.ListTopLevel()) Windows.Add(w);
    }

    [RelayCommand]
    private void ChooseWindow(WindowInfo? w)
    {
        if (w == null) return;
        Change(() =>
        {
            RunSettings.Target = TargetKind.Window;
            RunSettings.WindowTitle = w.Title;
            RunSettings.ProcessName = w.ProcessName;
        });
    }

    // Avalia a condição do passo selecionado agora, sem rodar o fluxo.
    [RelayCommand]
    private void TestCondition()
    {
        if (SelectedStep is not ConditionalStepBase cs) return;
        IPixelReader reader;
        if (RunSettings.Target == TargetKind.Window)
        {
            var w = WindowFinder.Find(RunSettings.WindowTitle, RunSettings.ProcessName);
            if (w == null)
            {
                _host.Toast("Janela alvo não encontrada para testar.", ToastKind.Warning);
                return;
            }
            reader = RunSettings.InputMode == InputMode.Background
                ? new WindowPixelReader(w.Handle)
                : new ScreenPixelReader(w.Handle);
        }
        else reader = new ScreenPixelReader(IntPtr.Zero);

        var window = RunSettings.Target == TargetKind.Window
            ? WindowFinder.Find(RunSettings.WindowTitle, RunSettings.ProcessName)?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;
        try
        {
            var ok = ConditionEvaluator.Evaluate(cs.Condition, reader, new Win32WindowQuery(), out var detail,
                Services.RunEnvironmentFactory.SpaceFor(Flow, window));
            _host.Toast($"{(ok ? "SIM" : "NÃO")}: {detail}", ok ? ToastKind.Success : ToastKind.Warning);
        }
        catch (Exception ex)
        {
            _host.Toast(ex.Message, ToastKind.Error);
        }
    }

    // Arrasto gravado: troca o trajeto exato por uma linha reta (para editar início/fim).
    [RelayCommand]
    private void ClearDragPath()
    {
        if (SelectedStep is not DragStep { HasPath: true } d) return;
        Change(() =>
        {
            d.DurationMs = Math.Max(60, d.DurationMs);
            d.HoldMs = 40;
            d.Path = [];
        });
    }

    [RelayCommand]
    private void SetWait(string ms)
    {
        if (SelectedStep is WaitStep w && int.TryParse(ms, out var v)) Change(() =>
        {
            w.Random = false;
            w.Ms = v;
        });
    }
}
