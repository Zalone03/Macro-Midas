using MacroMidas.Capture;
using MacroMidas.Input;
using MacroMidas.Native;

namespace MacroMidas.Engine;

public interface IWindowQuery
{
    bool Exists(string titleContains);
    bool IsActive(string titleContains);
}

public sealed class Win32WindowQuery : IWindowQuery
{
    public bool Exists(string titleContains) => WindowFinder.ExistsByTitle(titleContains);
    public bool IsActive(string titleContains) => WindowFinder.IsActiveByTitle(titleContains);
}

// Tudo que o motor precisa para rodar um fluxo: mouse/teclado, leitura de pixels e janelas.
public sealed class RunEnvironment : IDisposable
{
    public required IInputBackend Input { get; init; }
    public required IPixelReader Pixels { get; init; }
    public IWindowQuery Windows { get; init; } = new Win32WindowQuery();

    // Janela alvo resolvida (Zero quando o alvo é a tela).
    public IntPtr TargetWindow { get; init; }
    public string TargetDescription { get; init; } = "tela inteira";

    // Converte as coordenadas do fluxo em pixels (proporcional → tela/janela real).
    public CoordinateSpace Space { get; init; } = CoordinateSpace.Identity;

    public void Dispose() => Input.Dispose();
}

public sealed class RunOptions
{
    public bool HighPrecisionTimer { get; init; } = true;
    public int MoveStepMs { get; init; } = 8;
    public bool SkipStartDelay { get; init; }
    public bool LogConditions { get; init; } = true;
}

public enum RunState { Idle, Running, Paused, Stopping }

public enum RunEndReason { Completed, Stopped, Killed, Error }

public enum StopRequestResult { None, Graceful, Killed }

public sealed record RunFinishedInfo(RunEndReason Reason, string? Error, int Iterations, TimeSpan Elapsed);

public enum LogLevel { Info, Success, Warning, Error }

public sealed record RunLogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss.fff");
}
