using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroMidas.Models;
using MacroMidas.Services;

namespace MacroMidas.ViewModels;

// Tela de configurações avançadas (salva sozinha, com predefinições prontas).
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly Func<Task>? _preview;
    private readonly Action<string, ToastKind> _toast;
    private readonly DispatcherTimer _saveTimer;

    public AppSettings Settings => _service.Current;
    public OverlaySettings Overlay => Settings.Overlay;
    public HotkeySettings Hotkeys => Settings.Hotkeys;
    public RecordingSettings Recording => Settings.Recording;
    public ExecutionSettings Execution => Settings.Execution;
    public IReadOnlyList<SettingsPreset> Presets => SettingsPresets.All;
    public IReadOnlyList<Choice> PhysicalMouseChoices => Labels.PhysicalMouseActions;

    public bool IsAdministrator => Elevation.IsAdministrator;
    public string DataFolder => AppPaths.Root;
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    [ObservableProperty] private string? _hotkeyWarning;
    [ObservableProperty] private bool _isPreviewing;

    // Disparado quando os atalhos mudam (para registrar de novo).
    public event Action? HotkeysChanged;

    public SettingsViewModel(SettingsService service, Func<Task>? preview, Action<string, ToastKind> toast)
    {
        _service = service;
        _preview = preview;
        _toast = toast;
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) =>
        {
            _saveTimer!.Stop();
            _service.Save();
        });

        Settings.PropertyChanged += OnAnyChanged;
        Overlay.PropertyChanged += OnAnyChanged;
        Recording.PropertyChanged += OnAnyChanged;
        Execution.PropertyChanged += OnAnyChanged;
        Hotkeys.PropertyChanged += (s, e) =>
        {
            OnAnyChanged(s, e);
            HotkeysChanged?.Invoke();
        };
    }

    private void OnAnyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        _service.Save();
    }

    [RelayCommand]
    private void ApplyPreset(SettingsPreset? preset)
    {
        if (preset == null) return;
        preset.Apply(Settings);
        _toast($"Predefinição “{preset.Name}” aplicada", ToastKind.Success);
    }

    [RelayCommand]
    private void ResetHotkeys()
    {
        Hotkeys.CopyFrom(new HotkeySettings());
        _toast("Atalhos restaurados (F6 · F7 · F8 · F9)", ToastKind.Success);
    }

    [RelayCommand]
    private void ResetAll()
    {
        var d = new AppSettings();
        Overlay.CopyFrom(d.Overlay);
        Recording.CopyFrom(d.Recording);
        Execution.CopyFrom(d.Execution);
        Hotkeys.CopyFrom(d.Hotkeys);
        Settings.ConfirmDelete = true;
        _toast("Configurações restauradas para o padrão", ToastKind.Success);
    }

    [RelayCommand]
    private async Task Preview()
    {
        if (_preview == null || IsPreviewing) return;
        IsPreviewing = true;
        try
        {
            await _preview();
        }
        finally
        {
            IsPreviewing = false;
        }
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        SaveNow();
        if (Elevation.RestartAsAdministrator())
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        else
            _toast("Reinício como administrador cancelado.", ToastKind.Warning);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            AppPaths.Ensure();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _toast(ex.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private void SetOverlayColor(string hex) => Overlay.Color = hex;
}
