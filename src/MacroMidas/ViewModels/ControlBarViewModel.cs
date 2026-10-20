using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MacroMidas.ViewModels;

// Estado da barra translúcida que aparece embaixo da tela durante a execução/gravação.
public partial class ControlBarViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _stopPending;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isLock;
    [ObservableProperty] private double _restOpacity = 0.6;

    public string PauseLabel => IsPaused ? "Retomar" : "Pausar";
    public string StopLabel => StopPending ? "Parar já" : "Parar";
    public string StopHint => StopPending ? "Clique de novo para parar na hora" : "1 clique: termina a volta e para · 2 cliques: para na hora";

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(PauseLabel));

    partial void OnStopPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(StopLabel));
        OnPropertyChanged(nameof(StopHint));
    }

    public required IRelayCommand PauseCommand { get; init; }
    public required IRelayCommand StopCommand { get; init; }
    public required IRelayCommand StopRecordingCommand { get; init; }
    public required IRelayCommand CancelRecordingCommand { get; init; }
}
