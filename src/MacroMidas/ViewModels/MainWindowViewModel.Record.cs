using CommunityToolkit.Mvvm.Input;
using MacroMidas.Hooks;
using MacroMidas.Models;
using MacroMidas.Native;

namespace MacroMidas.ViewModels;

public partial class MainWindowViewModel
{
    private Recorder? _recorder;
    private CancellationTokenSource? _countdown;
    private IntPtr _recordTarget;

    [RelayCommand]
    private Task ToggleRecord() => ToggleRecordAsync();

    public async Task ToggleRecordAsync()
    {
        if (IsRecording) StopRecording(keep: true);
        else await StartRecordingAsync();
    }

    private async Task StartRecordingAsync()
    {
        if (Runner.IsBusy || IsRecording || Picker?.IsPicking == true) return;
        if (Editor == null) NewFlow();
        var flow = Editor!.Flow;

        var target = IntPtr.Zero;
        if (flow.Settings.Target == TargetKind.Window)
        {
            var w = WindowFinder.Find(flow.Settings.WindowTitle, flow.Settings.ProcessName);
            if (w == null)
            {
                Toast("Abra a janela alvo: a gravação usa posições relativas a ela.", ToastKind.Warning);
                return;
            }
            target = w.Handle;
        }
        _recordTarget = target;

        var rec = Settings.Recording;
        IsRecording = true;
        Bar.Title = $"Gravando · {flow.Name}";
        Bar.IsRecording = true;
        Bar.IsLock = false;
        Bar.IsPaused = false;
        Bar.StopPending = false;
        StatusText = "Preparando gravação…";

        if (rec.MinimizeOnRecord) Host?.MinimizeForSession();
        Overlays?.ShowSession(Settings, recording: true);

        _countdown = new CancellationTokenSource();
        var token = _countdown.Token;
        try
        {
            for (var i = Math.Max(0, rec.CountdownSeconds); i > 0; i--)
            {
                Bar.Status = $"Começando em {i}…";
                Overlays?.ShowCountdown(i, "Gravando em…");
                await Task.Delay(1000, token);
            }
        }
        catch (TaskCanceledException)
        {
            Overlays?.HideCountdown();
            return;
        }
        Overlays?.HideCountdown();
        if (!IsRecording) return;

        _recorder = new Recorder(rec, target, HotkeyVks());
        if (!_recorder.Start())
        {
            Toast("Não foi possível iniciar a gravação (o Windows recusou o hook).", ToastKind.Error);
            StopRecording(keep: false);
            return;
        }
        StatusText = "Gravando…";
        _uiTimer.Start();
    }

    public void StopRecording(bool keep, bool fromApp = false)
    {
        if (!IsRecording) return;
        _countdown?.Cancel();
        var steps = _recorder?.Stop(trimTrailingMoves: fromApp) ?? [];
        _recorder = null;
        IsRecording = false;
        Bar.IsRecording = false;
        _uiTimer.Stop();
        Overlays?.HideCountdown();
        Overlays?.HideSession();
        Host?.RestoreAfterSession();
        StatusText = "Pronto";
        StatusDetail = "";

        if (!keep)
        {
            Toast("Gravação descartada.");
            return;
        }
        if (steps.Count == 0)
        {
            Toast("Nada foi gravado.", ToastKind.Warning);
            return;
        }
        if (Editor?.Flow.Settings.IsProportional == true)
        {
            var space = Services.RunEnvironmentFactory.ProportionalSpace(_recordTarget);
            Engine.CoordinateMap.Convert(steps, space.ToFlow, space.DeltaToFlow);
        }
        if (Native.WindowFinder.ListTopLevel().Any(w => Native.WindowFinder.GameName(w.Handle) != null))
            foreach (var d in Flow.Walk(steps).OfType<DragStep>())
                d.RelativeMotion = true;
        Editor?.InsertSteps(steps);
        if (Editor?.Flow is { } flow && flow.Settings.IsLock)
        {
            flow.Settings.InputMode = InputMode.Direct;
            Toast($"{steps.Count} passos gravados. Para repetir igual a você (inclusive em jogos), este fluxo usa o mouse e o " +
                  "teclado de verdade: enquanto ele roda, não mexa (mexer pausa · F8 para).", ToastKind.Success);
        }
        else Toast($"{steps.Count} passos gravados. Revise e ajuste no editor.", ToastKind.Success);
    }

    private void UpdateRecordingUi()
    {
        if (_recorder == null) return;
        var n = _recorder.EventCount;
        Bar.Status = $"{n} evento(s) · {Input.HotkeyList.First(Settings.Hotkeys.Record)} ou Concluir para terminar";
        StatusDetail = $"{n} evento(s)";
    }
}
