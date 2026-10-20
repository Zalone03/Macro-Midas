using System.Diagnostics;
using Avalonia;
using MacroMidas.Services;
using static MacroMidas.Native.Win32;

namespace MacroMidas;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        CrashLog.Install();

        if (args is not ["--selftest", ..])
        {
            _singleInstance = new Mutex(true, @"Local\MacroMidas.SingleInstance", out var created);
            if (!created)
            {
                BringExistingToFront();
                return;
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(_singleInstance);
    }

    // Usado também pelo designer/visualizador do Avalonia.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void BringExistingToFront()
    {
        using var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            using (p)
            {
                if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(p.MainWindowHandle);
            }
        }
    }
}
