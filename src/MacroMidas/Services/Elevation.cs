using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Services;

// O Windows (UIPI) bloqueia entrada simulada para janelas de administrador sem privilégios.
public static class Elevation
{
    public static bool IsAdministrator { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // True se o processo dono da janela roda elevado (ou não deu para consultar: provável admin).
    public static bool IsProcessElevated(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return true;
        try
        {
            if (!OpenProcessToken(h, TOKEN_QUERY, out var token)) return true;
            try
            {
                return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(h);
        }
    }

    public static bool RestartAsAdministrator()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return false;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
