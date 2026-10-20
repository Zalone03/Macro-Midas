using CommunityToolkit.Mvvm.ComponentModel;

namespace MacroMidas.Models;

// Configurações globais do app.
public partial class AppSettings : ObservableObject
{
    public OverlaySettings Overlay { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
    public ExecutionSettings Execution { get; set; } = new();

    [ObservableProperty] private string? _lastFlowId;
    [ObservableProperty] private bool _confirmDelete = true;

    // Os fluxos de exemplo já foram criados uma vez (não recriar se o usuário apagar).
    [ObservableProperty] private bool _examplesSeeded;

    [ObservableProperty] private bool _showRunSettings = true;

    // Os exemplos antigos (vários fluxos) já foram trocados só pelo Auto-clique.
    [ObservableProperty] private bool _examplesCleaned;

    [ObservableProperty] private bool _alwaysOnTop;

    // Fluxos gravados já foram passados para o mouse/teclado de verdade (uma vez).
    [ObservableProperty] private bool _recordedFlowsUseRealInput;

    // Versão do arquivo de configurações (para migrar padrões antigos).
    [ObservableProperty] private int _settingsVersion;
}

public partial class OverlaySettings : ObservableObject
{
    // Borda dourada pulsando na tela inteira durante a execução.
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private double _opacity = 0.35;
    [ObservableProperty] private int _thickness = 2;
    [ObservableProperty] private int _glowSize = 18;
    [ObservableProperty] private double _pulseSeconds = 1.6;
    [ObservableProperty] private string _color = "#FFC83D";
    [ObservableProperty] private string _recordColor = "#FF6B4A";

    // Moldura sobre a janela "travada" no modo Lock.
    [ObservableProperty] private bool _lockFrame = true;

    // No modo Lock, bloquear o mouse do usuário sobre a janela alvo.
    [ObservableProperty] private bool _lockBlocksMouse = true;

    [ObservableProperty] private bool _controlBar = true;
    [ObservableProperty] private double _controlBarOpacity = 0.5;
    [ObservableProperty] private ControlBarPlacement _controlBarPlacement = ControlBarPlacement.Bottom;

    public void CopyFrom(OverlaySettings o)
    {
        Enabled = o.Enabled; Opacity = o.Opacity; Thickness = o.Thickness; GlowSize = o.GlowSize;
        PulseSeconds = o.PulseSeconds; Color = o.Color; RecordColor = o.RecordColor; LockFrame = o.LockFrame;
        LockBlocksMouse = o.LockBlocksMouse; ControlBar = o.ControlBar; ControlBarOpacity = o.ControlBarOpacity;
        ControlBarPlacement = o.ControlBarPlacement;
    }
}

public partial class HotkeySettings : ObservableObject
{
    [ObservableProperty] private string _start = "F6";
    [ObservableProperty] private string _pause = "F7";

    // 1x = termina o ciclo e para; 2x = mata na hora.
    [ObservableProperty] private string _stop = "F8";

    [ObservableProperty] private string _record = "F9";

    public void CopyFrom(HotkeySettings o)
    {
        Start = o.Start; Pause = o.Pause; Stop = o.Stop; Record = o.Record;
    }
}

public partial class RecordingSettings : ObservableObject
{
    // Grava cada movimento do mouse (o caminho exato, no mesmo ritmo).
    [ObservableProperty] private bool _recordMouseMoves = true;
    [ObservableProperty] private int _moveMinIntervalMs;
    [ObservableProperty] private int _moveMinDistance;
    [ObservableProperty] private bool _recordKeyboard = true;
    [ObservableProperty] private bool _recordScroll = true;
    [ObservableProperty] private bool _recordWaits = true;
    [ObservableProperty] private int _minWaitMs = 40;
    [ObservableProperty] private double _waitScale = 1.0;
    [ObservableProperty] private int _dragThreshold = 6;
    [ObservableProperty] private int _longPressMs = 450;
    [ObservableProperty] private bool _minimizeOnRecord = true;
    [ObservableProperty] private int _countdownSeconds = 3;

    public void CopyFrom(RecordingSettings o)
    {
        RecordMouseMoves = o.RecordMouseMoves; MoveMinIntervalMs = o.MoveMinIntervalMs; MoveMinDistance = o.MoveMinDistance;
        RecordKeyboard = o.RecordKeyboard; RecordScroll = o.RecordScroll; RecordWaits = o.RecordWaits;
        MinWaitMs = o.MinWaitMs; WaitScale = o.WaitScale; DragThreshold = o.DragThreshold; LongPressMs = o.LongPressMs;
        MinimizeOnRecord = o.MinimizeOnRecord; CountdownSeconds = o.CountdownSeconds;
    }
}

public partial class ExecutionSettings : ObservableObject
{
    [ObservableProperty] private bool _minimizeOnRun = true;
    [ObservableProperty] private bool _restoreAfterRun = true;
    [ObservableProperty] private bool _highPrecisionTimer = true;

    // Mover o mouse físico até o canto superior esquerdo mata o fluxo (modo direto).
    [ObservableProperty] private bool _failsafeCorner = true;

    // Modo Direto: mexer o mouse físico pausa o fluxo (assim o usuário sempre retoma o controle).
    [ObservableProperty] private PhysicalMouseAction _onPhysicalMouse = PhysicalMouseAction.Ignore;
    [ObservableProperty] private int _defaultClickHoldMs = 30;
    [ObservableProperty] private int _moveStepMs = 8;

    public void CopyFrom(ExecutionSettings o)
    {
        MinimizeOnRun = o.MinimizeOnRun; RestoreAfterRun = o.RestoreAfterRun; HighPrecisionTimer = o.HighPrecisionTimer;
        FailsafeCorner = o.FailsafeCorner; OnPhysicalMouse = o.OnPhysicalMouse; DefaultClickHoldMs = o.DefaultClickHoldMs;
        MoveStepMs = o.MoveStepMs;
    }
}
