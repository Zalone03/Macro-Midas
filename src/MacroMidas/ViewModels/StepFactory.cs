using MacroMidas.Models;

namespace MacroMidas.ViewModels;

public sealed record PaletteItem(string Kind, string Label, string Icon, StepCategory Category, string Tip);

public sealed record PaletteGroup(string Name, IReadOnlyList<PaletteItem> Items);

// Cria passos novos com valores padrão sensatos.
public static class StepFactory
{
    public static IReadOnlyList<PaletteGroup> Groups { get; } =
    [
        new("Mouse",
        [
            new("click", "Clique", "Click", StepCategory.Mouse, "Clique simples, duplo ou vários, numa posição ou onde o mouse estiver"),
            new("move", "Mover", "Move", StepCategory.Mouse, "Move o cursor (instantâneo ou suave)"),
            new("drag", "Arrastar", "Drag", StepCategory.Mouse, "Segura o botão e arrasta de um ponto a outro"),
            new("button", "Segurar/Soltar", "Hold", StepCategory.Mouse, "Mantém um botão pressionado (ou solta)"),
            new("scroll", "Rolar", "Scroll", StepCategory.Mouse, "Rodinha do mouse num ponto da tela (você escolhe onde)"),
        ]),
        new("Teclado",
        [
            new("key", "Tecla", "Key", StepCategory.Keyboard, "Aperta, segura ou solta uma tecla ou atalho (Ctrl+C…)"),
            new("type", "Digitar", "Type", StepCategory.Keyboard, "Digita um texto (acentos e emojis funcionam)"),
        ]),
        new("Tempo",
        [
            new("wait", "Esperar", "Wait", StepCategory.Time, "Pausa fixa ou aleatória"),
        ]),
        new("Lógica",
        [
            new("if", "Se…", "If", StepCategory.Logic, "Pergunta antes de seguir: SE a cor/janela for X, faz A; SENÃO, faz B"),
            new("waitUntil", "Esperar até", "WaitUntil", StepCategory.Logic, "Fica esperando até uma cor/janela aparecer"),
            new("repeat", "Repetir", "Repeat", StepCategory.Logic, "Repete um bloco N vezes ou infinitamente"),
            new("hold", "Segurar enquanto", "Hold", StepCategory.Logic, "Segura uma tecla/botão enquanto os passos de dentro rodam (ex.: segurar W e clicar)"),
            new("parallel", "Ao mesmo tempo", "Parallel", StepCategory.Logic, "Roda trilhas lado a lado, ao mesmo tempo (ex.: segurar W numa e clicar na outra)"),
            new("stop", "Parar / Pular", "StopFlow", StepCategory.Logic, "Para o fluxo, pula o ciclo ou sai do Repetir"),
        ]),
    ];

    public static IEnumerable<PaletteItem> All => Groups.SelectMany(g => g.Items);

    public static FlowStep Create(string kind, AppSettings? settings = null)
    {
        var hold = settings?.Execution.DefaultClickHoldMs ?? 30;
        return kind switch
        {
            "move" => new MoveStep { X = 960, Y = 540, DurationMs = 150 },
            "click" => new ClickStep { X = 960, Y = 540, HoldMs = hold },
            "button" => new MouseButtonStep(),
            "drag" => new DragStep { FromX = 800, FromY = 540, ToX = 1100, ToY = 540 },
            "scroll" => new ScrollStep { UsePosition = true, X = 960, Y = 540, ClickFirst = true },
            "key" => new KeyStep { Keys = "Enter", HoldMs = hold },
            "type" => new TypeTextStep { Text = "Olá!" },
            "wait" => new WaitStep { Ms = 500 },
            "if" => NewIf(),
            "waitUntil" => new WaitUntilStep(),
            "repeat" => new RepeatStep { Count = 3 },
            "hold" => new HoldStep { Kind = HoldInputKind.Key, Keys = "W" },
            "parallel" => new ParallelStep(),
            "stop" => new StopStep(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static IfStep NewIf()
    {
        var s = new IfStep { Condition = new Condition { X = 960, Y = 540 } };
        return s;
    }
}
