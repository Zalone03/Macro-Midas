using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MacroMidas.ViewModels;

public enum ToastKind { Info, Success, Warning, Error }

public partial class ToastViewModel(string message, ToastKind kind) : ObservableObject
{
    public string Message { get; } = message;
    public ToastKind Kind { get; } = kind;

    public string Icon => Kind switch
    {
        ToastKind.Success => "Check",
        ToastKind.Warning => "Warning",
        ToastKind.Error => "Warning",
        _ => "Info"
    };

    public bool IsError => Kind == ToastKind.Error;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsSuccess => Kind == ToastKind.Success;
}

// Diálogo de confirmação dentro da janela (com fundo escurecido).
public partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = "Confirmar";
    public string CancelText { get; init; } = "Cancelar";
    public bool IsDanger { get; init; }

    public Task<bool> Result => _tcs.Task;

    [RelayCommand]
    private void Confirm() => _tcs.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult(false);
}
