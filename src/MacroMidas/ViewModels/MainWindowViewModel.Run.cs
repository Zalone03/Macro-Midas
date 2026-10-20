using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MacroMidas.Engine;
using MacroMidas.Hooks;
using MacroMidas.Input;
using MacroMidas.Models;
using MacroMidas.Services;
using static MacroMidas.Native.Win32;

namespace MacroMidas.ViewModels;

public partial class MainWindowViewModel
{
    private LowLevelInputHook? _guardHook;
    private readonly Dictionary<HotkeyAction, long> _lastHotkey = [];
    private long _resumedAt;
    private FlowStep? _highlighted;

    [RelayCommand]
    private void StartSelected()
    {
        if (SelectedFlow != null) StartFlow(SelectedFlow);
    }

    private void OnStartHotkey()
    {
        if (Runner.State == RunState.Paused) Runner.Resume();
        else if (!IsBusy && SelectedFlow != null) StartFlow(SelectedFlow);
    }

    public void StartFlow(Flow flow)
    {
        if (Runner.IsBusy)
        {
            if (Runner.State == RunState.Paused) Runner.Resume();
            return;
        }
        if (IsRecording || Picker?.IsPicking == true) return;
        if (!Flow.Walk(flow.Steps).Any(s => s.Enabled))
        {
            Toast("Esse fluxo ainda não tem passos ativos.", ToastKind.Warning);
            return;
        }

        if (ReferenceEquals(flow, SelectedFlow)) Editor?.TrackChanges();
        var game = FindGame(flow);
        if (flow.Settings.IsLock && game != null)
            flow.Settings.InputMode = InputMode.Direct;
        var gameMode = game != null;
        if (gameMode)
            Toast($"{game} detectado: rodando enxuto (sem borda nem barra) para não atrapalhar. Pare com {Input.HotkeyList.First(Settings.Hotkeys.Stop)}.", ToastKind.Warning);
        TrySave(flow);

        var res = RunEnvironmentFactory.Create(flow);
        if (res.Environment is not { } env)
        {
            Toast(res.Error ?? "Não foi possível preparar a execução.", ToastKind.Error);
            return;
        }
        if (res.Warning != null) Toast(res.Warning, ToastKind.Warning);

        var exec = Settings.Execution;
        Log.Clear();
        Bar.Title = flow.Name;
        Bar.IsLock = flow.Settings.IsLock;
        Bar.IsRecording = false;
        Bar.IsPaused = false;
        Bar.StopPending = false;
        Bar.Status = flow.Settings.IsLock ? "Iniciando…" : "Iniciando · usa seu mouse e teclado: não mexa (F8 para)";

        if (exec.MinimizeOnRun && !flow.Settings.IsLock) Host?.MinimizeForSession();
        if (!gameMode)
        {
            Overlays?.ShowSession(Settings, recording: false);
            if (flow.Settings.IsLock && env.TargetWindow != IntPtr.Zero) Overlays?.ShowLock(env.TargetWindow, Settings);
        }
        if (!gameMode)
            StartGuard(direct: !flow.Settings.IsLock,
                graceMs: (int)(flow.Settings.StartDelayMs / Math.Clamp(flow.Settings.Speed, 0.05, 20)) + 250);

        var options = new RunOptions
        {
            HighPrecisionTimer = exec.HighPrecisionTimer,
            MoveStepMs = exec.MoveStepMs,
        };
        if (!Runner.Start(flow, env, options))
        {
            env.Dispose();
            EndSessionUi();
            return;
        }
        RunState = Runner.State;
        StatusText = $"Executando “{flow.Name}”";
        _uiTimer.Start();
    }

    // Jogo onde o fluxo vai agir: sob o cursor, sob os pontos fixos ou na janela alvo.
    private static string? FindGame(Flow flow)
    {
        var s = flow.Settings;
        if (s.Target == TargetKind.Window)
            return Native.WindowFinder.Find(s.WindowTitle, s.ProcessName) is { } w ? Native.WindowFinder.GameName(w.Handle) : null;

        string? At(int x, int y) => Native.WindowFinder.GameName(WindowFromPoint(new POINT(x, y)));

        GetCursorPos(out var cursor);
        if (At(cursor.X, cursor.Y) is { } underCursor) return underCursor;
        foreach (var step in Flow.Walk(flow.Steps))
        {
            var found = step switch
            {
                ClickStep { UsePosition: true } c => At(c.X, c.Y),
                MouseButtonStep { UsePosition: true } b => At(b.X, b.Y),
                ScrollStep { UsePosition: true } sc => At(sc.X, sc.Y),
                DragStep d => At(d.FromX, d.FromY),
                _ => null
            };
            if (found != null) return found;
        }
        foreach (var win in Native.WindowFinder.ListTopLevel())
            if (Native.WindowFinder.GameName(win.Handle) is { } g) return g;
        return null;
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (Runner.IsBusy) Runner.TogglePause();
    }

    // 1× termina o ciclo e para; 2× mata na hora.
    [RelayCommand]
    private void Stop()
    {
        if (IsRecording)
        {
            StopRecording(keep: true);
            return;
        }
        switch (Runner.RequestStop())
        {
            case StopRequestResult.Graceful:
                Bar.StopPending = true;
                Toast("Vai parar no fim desta volta. Aperte Parar de novo para parar na hora.");
                break;
            case StopRequestResult.Killed:
                Bar.StopPending = false;
                break;
        }
    }

    [RelayCommand]
    private void Kill() => Runner.Kill();

    private void HandleRunnerState(RunState s)
    {
        if (RunState == RunState.Paused && s is RunState.Running or RunState.Stopping) _resumedAt = Environment.TickCount64;
        RunState = s;
        Bar.IsPaused = s == RunState.Paused;
        Bar.StopPending = s == RunState.Stopping || (Runner.StopRequested && s != RunState.Idle);
    }

    private void OnRunFinished(RunFinishedInfo info)
    {
        _uiTimer.Stop();
        DrainLog();
        if (_highlighted != null) _highlighted.IsRunning = false;
        _highlighted = null;
        RunState = RunState.Idle;
        EndSessionUi();
        if (Settings.Execution.RestoreAfterRun) Host?.RestoreAfterSession();

        var name = Runner.FlowName;
        switch (info.Reason)
        {
            case RunEndReason.Completed:
                Toast($"“{name}” concluído · {info.Iterations} ciclo(s) em {info.Elapsed.TotalSeconds:0.0} s", ToastKind.Success);
                break;
            case RunEndReason.Stopped:
                Toast($"“{name}” parado ao fim do ciclo {info.Iterations}.");
                break;
            case RunEndReason.Killed:
                Toast($"“{name}” morto na hora.", ToastKind.Warning);
                break;
            case RunEndReason.Error:
                Toast($"Erro em “{name}”: {info.Error}", ToastKind.Error);
                IsLogOpen = true;
                break;
        }
        StatusText = "Pronto";
        StatusDetail = "";
    }

    private void EndSessionUi()
    {
        StopGuard();
        Overlays?.HideSession();
        Bar.StopPending = false;
        Bar.IsPaused = false;
    }

    private void OnUiTick()
    {
        if (IsRecording)
        {
            UpdateRecordingUi();
            return;
        }
        if (!Runner.IsBusy) return;

        var current = Runner.CurrentStep;
        if (!ReferenceEquals(current, _highlighted))
        {
            if (_highlighted != null) _highlighted.IsRunning = false;
            if (current != null) current.IsRunning = true;
            _highlighted = current;
        }

        var it = Runner.Iteration;
        var cycle = Runner.TotalIterations is { } total ? $"Ciclo {it}/{total}" : $"Ciclo {it} · contínuo";
        var step = current != null ? $" · {current.Title}" : "";
        Bar.Status = Runner.State switch
        {
            RunState.Paused => $"Pausado · {cycle}",
            RunState.Stopping => $"Vai parar no fim desta volta · {cycle}",
            _ => cycle + step
        };
        StatusText = Runner.State switch
        {
            RunState.Paused => $"Pausado · {Runner.FlowName}",
            RunState.Stopping => $"Terminando · {Runner.FlowName}",
            _ => $"Executando · {Runner.FlowName}"
        };
        StatusDetail = $"{cycle} · {Runner.Elapsed:mm\\:ss}";
        DrainLog();
    }

    private void DrainLog()
    {
        var added = 0;
        while (added < 200 && Runner.TryDequeueLog(out var e))
        {
            Log.Add(e);
            added++;
        }
        while (Log.Count > 400) Log.RemoveAt(0);
    }

    // Teclas de Iniciar/Pausar/Parar funcionam mesmo se um jogo bloquear os atalhos do Windows.
    private void StartGuard(bool direct, int graceMs = 0)
    {
        StopGuard();
        var since = System.Diagnostics.Stopwatch.StartNew();
        var exec = Settings.Execution;
        var h = Settings.Hotkeys;
        var watchUser = direct && exec.OnPhysicalMouse != PhysicalMouseAction.Ignore;
        var guard = new SessionGuard(
            [(HotkeyAction.Start, h.Start), (HotkeyAction.Pause, h.Pause), (HotkeyAction.Stop, h.Stop)],
            watchUser, corner: direct && exec.FailsafeCorner,
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN));

        guard.Hotkey += a => Dispatcher.UIThread.Post(() => OnHotkeyAction(a));
        guard.Corner += () => Dispatcher.UIThread.Post(() =>
        {
            if (!Runner.IsBusy) return;
            Runner.Kill();
            Toast("Saída de emergência: mouse no canto superior esquerdo, fluxo parado na hora.", ToastKind.Warning);
        });
        guard.UserTookOver += () => Dispatcher.UIThread.Post(() =>
        {
            if (Runner.State is not (RunState.Running or RunState.Stopping)) return;
            if (since.ElapsedMilliseconds < graceMs) return;
            if (Environment.TickCount64 - _resumedAt < 1500) return;
            if (exec.OnPhysicalMouse == PhysicalMouseAction.Pause)
            {
                Runner.Pause();
                Toast($"Pausado: você mexeu no mouse ou no teclado. {HotkeyList.First(h.Start)} ou Retomar continua.");
            }
            else
            {
                Runner.Kill();
                Toast("Parado: você mexeu no mouse ou no teclado.", ToastKind.Warning);
            }
        });

        _guardHook = new LowLevelInputHook(mouse: direct, keyboard: true);
        _guardHook.Event += guard.Process;
        if (!_guardHook.Start())
        {
            _guardHook.Dispose();
            _guardHook = null;
        }
    }

    // Tecla de Iniciar/Pausar/Parar, venha do atalho do Windows ou da trava acima.
    private void OnHotkeyAction(HotkeyAction action)
    {
        var now = Environment.TickCount64;
        if (_lastHotkey.TryGetValue(action, out var last) && now - last < 200) return;
        _lastHotkey[action] = now;
        switch (action)
        {
            case HotkeyAction.Start:
                OnStartHotkey();
                break;
            case HotkeyAction.Pause:
                TogglePause();
                break;
            case HotkeyAction.Stop:
                Stop();
                break;
        }
    }

    private void StopGuard()
    {
        _guardHook?.Dispose();
        _guardHook = null;
    }
}
