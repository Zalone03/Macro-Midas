using System.Collections.Concurrent;
using System.Diagnostics;
using MacroMidas.Models;
using MacroMidas.Native;
using MacroMidas.Serialization;

namespace MacroMidas.Engine;

// Executa fluxos numa thread dedicada (tempo preciso, UI livre).
public sealed partial class FlowRunner
{
    private Thread? _thread;
    private CancellationTokenSource _kill = new();
    private readonly ManualResetEventSlim _resume = new(true);
    private readonly Stopwatch _clock = new();
    private readonly Random _rng = new();
    private readonly ConcurrentQueue<RunLogEntry> _log = new();
    private Dictionary<FlowStep, FlowStep> _originals = new(ReferenceEqualityComparer.Instance);

    private RunEnvironment _env = null!;
    private RunOptions _opt = new();
    private double _speed = 1;
    private long _pausedTicks;

    private volatile RunState _state = RunState.Idle;
    private volatile bool _stopRequested;
    private volatile FlowStep? _current;
    private volatile int _iteration;

    public RunState State => _state;
    public bool IsBusy => _state != RunState.Idle;
    public bool StopRequested => _stopRequested;

    // Passo (do fluxo original, não da cópia) que está executando agora.
    public FlowStep? CurrentStep => _current;

    public int Iteration => _iteration;
    public int? TotalIterations { get; private set; }
    public string FlowName { get; private set; } = "";
    public Guid FlowId { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;

    // Disparado na thread que mudou o estado (UI ou motor).
    public event Action<RunState>? StateChanged;

    // Disparado na thread do motor ao terminar.
    public event Action<RunFinishedInfo>? Finished;

    public bool TryDequeueLog([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out RunLogEntry entry) =>
        _log.TryDequeue(out entry);

    public bool Start(Flow flow, RunEnvironment env, RunOptions options)
    {
        if (IsBusy) return false;

        var copy = MmfSerializer.Clone(flow);
        _originals = new Dictionary<FlowStep, FlowStep>(ReferenceEqualityComparer.Instance);
        foreach (var (c, o) in Flow.Walk(copy.Steps).Zip(Flow.Walk(flow.Steps)))
            _originals[c] = o;

        _env = env;
        _opt = options;
        _kill = new CancellationTokenSource();
        _resume.Set();
        _stopRequested = false;
        _pausedTicks = 0;
        _iteration = 0;
        _warnedLook = false;
        FlowName = flow.Name;
        FlowId = flow.Id;
        TotalIterations = flow.Settings.RepeatMode == RepeatMode.Infinite ? null : Math.Max(1, flow.Settings.RepeatCount);

        SetState(RunState.Running);
        _thread = new Thread(() => Run(copy))
        {
            IsBackground = true,
            Name = "MacroMidas.Runner",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
        return true;
    }

    public void Pause()
    {
        if (_state is not (RunState.Running or RunState.Stopping)) return;
        _resume.Reset();
        SetState(RunState.Paused);
        Log(LogLevel.Warning, "Pausado");
    }

    public void Resume()
    {
        if (_state != RunState.Paused) return;
        SetState(_stopRequested ? RunState.Stopping : RunState.Running);
        _resume.Set();
        Log(LogLevel.Info, "Retomado");
    }

    public void TogglePause()
    {
        if (_state == RunState.Paused) Resume();
        else Pause();
    }

    // 1ª chamada: parada suave (termina o ciclo).
    public StopRequestResult RequestStop()
    {
        if (!IsBusy) return StopRequestResult.None;
        if (!_stopRequested && _state != RunState.Paused)
        {
            _stopRequested = true;
            SetState(RunState.Stopping);
            Log(LogLevel.Warning, "Parada solicitada: terminando o ciclo atual (aperte de novo para matar)");
            return StopRequestResult.Graceful;
        }
        Kill();
        return StopRequestResult.Killed;
    }

    public void Kill()
    {
        if (!IsBusy) return;
        _stopRequested = true;
        _kill.Cancel();
        _resume.Set();
    }

    // Espera a thread do motor terminar (usado ao fechar o app e nos testes).
    public bool Join(TimeSpan timeout) => _thread?.Join(timeout) ?? true;

    private void SetState(RunState s)
    {
        if (_state == s) return;
        _state = s;
        StateChanged?.Invoke(s);
    }

    private void SetCurrent(FlowStep? copyStep) =>
        _current = copyStep != null && _originals.TryGetValue(copyStep, out var o) ? o : null;

    private void Log(LogLevel level, string message) => _log.Enqueue(new RunLogEntry(DateTime.Now, level, message));

    private void Run(Flow flow)
    {
        var reason = RunEndReason.Completed;
        string? error = null;
        var precise = _opt.HighPrecisionTimer;
        if (precise) Win32.timeBeginPeriod(1);
        _clock.Restart();

        try
        {
            var s = flow.Settings;
            _speed = Math.Clamp(s.Speed, 0.05, 20);
            Log(LogLevel.Info, $"Iniciando “{flow.Name}” · {_env.Input.Name} · alvo: {_env.TargetDescription}");

            if (!flow.Steps.Any(x => x.Enabled))
            {
                Log(LogLevel.Warning, "O fluxo não tem passos ativos.");
                return;
            }

            if (!_opt.SkipStartDelay && s.StartDelayMs > 0) Sleep(s.StartDelayMs);

            while (true)
            {
                _iteration++;
                var r = ExecList(flow.Steps);
                if (r == ExecResult.StopFlow)
                {
                    Log(LogLevel.Info, "Fluxo encerrado por um passo “Parar”.");
                    break;
                }
                if (_stopRequested) break;
                if (TotalIterations is { } total && _iteration >= total) break;
                Sleep(Math.Max(1, Scaled(s.IterationDelayMs)));
            }

            reason = _stopRequested ? RunEndReason.Stopped : RunEndReason.Completed;
        }
        catch (OperationCanceledException)
        {
            reason = RunEndReason.Killed;
        }
        catch (Exception ex)
        {
            reason = RunEndReason.Error;
            error = ex.Message;
            Log(LogLevel.Error, ex.Message);
        }
        finally
        {
            try { _env.Input.ReleaseAll(); } catch { }
            if (precise) Win32.timeEndPeriod(1);
            _clock.Stop();
            _current = null;
            var info = new RunFinishedInfo(reason, error, _iteration, _clock.Elapsed);
            Log(reason switch
            {
                RunEndReason.Completed => LogLevel.Success,
                RunEndReason.Error => LogLevel.Error,
                _ => LogLevel.Warning
            }, reason switch
            {
                RunEndReason.Completed => $"Concluído · {_iteration} ciclo(s) em {_clock.Elapsed.TotalSeconds:0.0} s",
                RunEndReason.Stopped => $"Parado ao fim do ciclo {_iteration}",
                RunEndReason.Killed => $"Morto na hora (ciclo {_iteration})",
                _ => "Execução interrompida por erro"
            });
            try { _env.Dispose(); } catch { }
            SetState(RunState.Idle);
            Finished?.Invoke(info);
        }
    }
}
