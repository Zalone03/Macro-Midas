using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace MacroMidas.Services;

// O que os ViewModels precisam da janela principal (diálogos, área de transferência, minimizar).
public interface IWindowHost
{
    Window Window { get; }
    IStorageProvider? Storage { get; }
    IClipboard? Clipboard { get; }

    void MinimizeForSession();

    void RestoreAfterSession();
}
