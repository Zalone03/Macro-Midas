using System.Text.Json;
using MacroMidas.Models;
using MacroMidas.Serialization;

namespace MacroMidas.Services;

public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json, MmfSerializer.Options) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Settings", ex);
            TryBackupCorrupt();
            Current = new AppSettings();
        }
        Migrate(Current);
        return Current;
    }

    private const int CurrentVersion = 5;

    // Atualiza valores que ainda estão em padrões antigos.
    private static void Migrate(AppSettings s)
    {
        if (s.SettingsVersion >= CurrentVersion) return;
        var o = s.Overlay;
        if (s.SettingsVersion < 2)
        {
            if (Math.Abs(o.Opacity - 0.55) < 0.001 && o.Thickness == 3 && o.GlowSize == 26)
            {
                o.Opacity = 0.35;
                o.Thickness = 2;
                o.GlowSize = 18;
            }
            if (Math.Abs(o.ControlBarOpacity - 0.6) < 0.001) o.ControlBarOpacity = 0.5;
        }
        if (s.SettingsVersion < 3)
        {
            if (s.Execution.OnPhysicalMouse == PhysicalMouseAction.Ignore) s.Execution.OnPhysicalMouse = PhysicalMouseAction.Pause;
        }
        if (s.SettingsVersion < 4)
        {
            s.Recording.RecordMouseMoves = true;
            s.Recording.MoveMinIntervalMs = 0;
            s.Recording.MoveMinDistance = 0;
        }
        if (s.SettingsVersion < 5)
        {
            if (s.Execution.OnPhysicalMouse == PhysicalMouseAction.Pause) s.Execution.OnPhysicalMouse = PhysicalMouseAction.Ignore;
        }
        s.SettingsVersion = CurrentVersion;
    }

    public void Save()
    {
        try
        {
            AppPaths.WriteAllTextAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, MmfSerializer.Options));
        }
        catch (Exception ex)
        {
            CrashLog.Write("SettingsSave", ex);
        }
    }

    private static void TryBackupCorrupt()
    {
        try
        {
            File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".corrompido", overwrite: true);
        }
        catch
        {
        }
    }
}

public sealed record SettingsPreset(string Name, string Description, Action<AppSettings> Apply);

// Predefinições prontas para usar (o usuário ainda pode ajustar tudo depois).
public static class SettingsPresets
{
    public static IReadOnlyList<SettingsPreset> All { get; } =
    [
        new("Equilibrado", "Padrão recomendado: borda suave, barra translúcida, tempo preciso.", s =>
        {
            var d = new AppSettings();
            s.Overlay.CopyFrom(d.Overlay);
            s.Execution.CopyFrom(d.Execution);
            s.Recording.CopyFrom(d.Recording);
        }),
        new("Discreto", "Quase invisível: borda fina e fraca, barra bem transparente.", s =>
        {
            s.Overlay.Enabled = true;
            s.Overlay.Opacity = 0.25;
            s.Overlay.Thickness = 2;
            s.Overlay.GlowSize = 12;
            s.Overlay.PulseSeconds = 2.4;
            s.Overlay.ControlBarOpacity = 0.35;
            s.Overlay.LockFrame = true;
        }),
        new("Destaque máximo", "Borda forte e pulso rápido: impossível esquecer que está rodando.", s =>
        {
            s.Overlay.Enabled = true;
            s.Overlay.Opacity = 0.9;
            s.Overlay.Thickness = 5;
            s.Overlay.GlowSize = 44;
            s.Overlay.PulseSeconds = 1.0;
            s.Overlay.ControlBarOpacity = 0.85;
        }),
        new("Desempenho", "Sem efeitos visuais na tela e gravação enxuta, para fluxos muito rápidos.", s =>
        {
            s.Overlay.Enabled = false;
            s.Overlay.LockFrame = false;
            s.Overlay.ControlBarOpacity = 0.5;
            s.Execution.HighPrecisionTimer = true;
            s.Execution.MoveStepMs = 5;
            s.Recording.MinWaitMs = 25;
        }),
    ];
}
