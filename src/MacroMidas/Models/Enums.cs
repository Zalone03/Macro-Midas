namespace MacroMidas.Models;

public enum StepCategory { Mouse, Keyboard, Time, Logic }

public enum MouseButtonKind { Left, Right, Middle, X1, X2 }

// Segurar (pressionar e manter) ou soltar um botão do mouse.
public enum ButtonAction { Press, Release }

public enum KeyAction { Press, Down, Up }

public enum ScrollDirection { Up, Down, Left, Right }

public enum ConditionKind { PixelColor, ColorInRegion, WindowExists, WindowActive }

public enum TimeoutAction { Continue, StopFlow, NextIteration }

public enum StopMode { StopFlow, NextIteration, BreakLoop }

public enum RepeatMode { Count, Infinite }

public enum TargetKind { Screen, Window }

// Pixels: posições em pixels fixos (só caem certo na mesma resolução).
public enum CoordinateMode { Pixels, Proportional }

// O que o bloco "Segurar enquanto" mantém apertado: uma tecla ou um botão do mouse.
public enum HoldInputKind { Key, MouseButton }

// Direct = SendInput (controla o cursor real, funciona em tudo).
public enum InputMode { Direct, Background }

public enum PhysicalMouseAction { Ignore, Pause, Stop }

public enum ControlBarPlacement { Bottom, Top }

// Opção de enum com rótulo em português, para ComboBox/segmentos.
public sealed record Choice(object Value, string Label);

public static class Labels
{
    public static string Of(MouseButtonKind b) => b switch
    {
        MouseButtonKind.Left => "esquerdo",
        MouseButtonKind.Right => "direito",
        MouseButtonKind.Middle => "do meio",
        MouseButtonKind.X1 => "lateral 1",
        MouseButtonKind.X2 => "lateral 2",
        _ => b.ToString()
    };

    public static string Of(ScrollDirection d) => d switch
    {
        ScrollDirection.Up => "↑ cima",
        ScrollDirection.Down => "↓ baixo",
        ScrollDirection.Left => "← esquerda",
        ScrollDirection.Right => "→ direita",
        _ => d.ToString()
    };

    public static string Of(TimeoutAction a) => a switch
    {
        TimeoutAction.Continue => "continuar",
        TimeoutAction.StopFlow => "parar o fluxo",
        TimeoutAction.NextIteration => "próximo ciclo",
        _ => a.ToString()
    };

    public static IReadOnlyList<Choice> MouseButtons { get; } =
    [
        new(MouseButtonKind.Left, "Esquerdo"),
        new(MouseButtonKind.Right, "Direito"),
        new(MouseButtonKind.Middle, "Meio"),
        new(MouseButtonKind.X1, "Lateral 1"),
        new(MouseButtonKind.X2, "Lateral 2"),
    ];

    public static IReadOnlyList<Choice> ConditionKinds { get; } =
    [
        new(ConditionKind.PixelColor, "Cor de um pixel"),
        new(ConditionKind.ColorInRegion, "Cor dentro de uma área"),
        new(ConditionKind.WindowExists, "Janela existe"),
        new(ConditionKind.WindowActive, "Janela está em foco"),
    ];

    public static IReadOnlyList<Choice> TimeoutActions { get; } =
    [
        new(TimeoutAction.Continue, "Continuar o fluxo"),
        new(TimeoutAction.StopFlow, "Parar o fluxo"),
        new(TimeoutAction.NextIteration, "Pular para o próximo ciclo"),
    ];

    public static IReadOnlyList<Choice> StopModes { get; } =
    [
        new(StopMode.StopFlow, "Parar o fluxo inteiro"),
        new(StopMode.NextIteration, "Ir para o próximo ciclo"),
        new(StopMode.BreakLoop, "Sair do Repetir atual"),
    ];

    public static IReadOnlyList<Choice> PhysicalMouseActions { get; } =
    [
        new(PhysicalMouseAction.Ignore, "Ignorar"),
        new(PhysicalMouseAction.Pause, "Pausar o fluxo"),
        new(PhysicalMouseAction.Stop, "Parar o fluxo"),
    ];
}
