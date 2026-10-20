using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Native;

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, int ProcessId)
{
    public string Display => string.IsNullOrEmpty(ProcessName) ? Title : $"{Title}  ·  {ProcessName}";
}

// Enumeração e consulta de janelas de nível superior.
public static class WindowFinder
{
    private static readonly int OwnPid = Environment.ProcessId;

    public static string GetTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLengthW(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName + ".exe";
        }
        catch
        {
            return "";
        }
    }

    public static int GetProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid;
    }

    public static bool IsOwnWindow(IntPtr hwnd) => hwnd != IntPtr.Zero && GetProcessId(GetAncestor(hwnd, GA_ROOT)) == OwnPid;

    public static string GetClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static readonly string[] GameClasses = ["WINDOWSCLIENT", "UnityWndClass", "UnrealWindow"];
    private static readonly string[] GameProcesses = ["RobloxPlayerBeta.exe", "RobloxPlayer.exe", "Roblox.exe"];

    // Nome do jogo se ele lê entrada direto do hardware (Roblox, Unity, Unreal) e ignora o Lock.
    public static string? GameName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        var root = GetAncestor(hwnd, GA_ROOT);
        if (root == IntPtr.Zero || IsOwnWindow(root)) return null;
        var process = GetProcessName(GetProcessId(root));
        if (GameProcesses.Any(p => p.Equals(process, StringComparison.OrdinalIgnoreCase))) return "O Roblox";
        var cls = GetClassName(root);
        if (cls.Equals("WINDOWSCLIENT", StringComparison.Ordinal)) return "O Roblox";
        return GameClasses.Contains(cls, StringComparer.Ordinal) ? "Esse jogo" : null;
    }

    public static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    // Janelas "de verdade" (as que aparecem no Alt+Tab), na ordem Z (de cima para baixo).
    public static List<WindowInfo> ListTopLevel(bool includeOwn = false) =>
        [.. Candidates(includeOwn).Select(c => new WindowInfo(c.Hwnd, c.Title, GetProcessName(c.Pid), c.Pid))];

    private static List<(IntPtr Hwnd, string Title, int Pid)> Candidates(bool includeOwn)
    {
        var result = new List<(IntPtr, string, int)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsCloaked(hwnd)) return true;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return true;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return true;
            var title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            var pid = GetProcessId(hwnd);
            if (!includeOwn && pid == OwnPid) return true;
            result.Add((hwnd, title, pid));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // Procura por parte do título e/ou nome do processo (sem diferenciar maiúsculas).
    public static WindowInfo? Find(string? titleContains, string? processName)
    {
        var title = titleContains?.Trim() ?? "";
        var proc = NormalizeProcess(processName);
        if (title.Length == 0 && proc.Length == 0) return null;

        foreach (var (hwnd, t, pid) in Candidates(includeOwn: false))
        {
            if (title.Length > 0 && !t.Contains(title, StringComparison.OrdinalIgnoreCase)) continue;
            var name = GetProcessName(pid);
            if (proc.Length > 0 && !NormalizeProcess(name).Equals(proc, StringComparison.OrdinalIgnoreCase)) continue;
            return new WindowInfo(hwnd, t, name, pid);
        }
        return null;
    }

    private static string NormalizeProcess(string? p)
    {
        var s = p?.Trim() ?? "";
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    public static bool ExistsByTitle(string titleContains) =>
        !string.IsNullOrWhiteSpace(titleContains) &&
        Candidates(includeOwn: false).Any(c => c.Title.Contains(titleContains.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsActiveByTitle(string titleContains)
    {
        if (string.IsNullOrWhiteSpace(titleContains)) return false;
        var fg = GetForegroundWindow();
        return fg != IntPtr.Zero && GetTitle(fg).Contains(titleContains.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // Janela de nível superior visível sob um ponto da tela, ignorando as do próprio app.
    public static WindowInfo? TopLevelAt(int x, int y)
    {
        foreach (var (hwnd, title, pid) in Candidates(includeOwn: false))
        {
            if (IsIconic(hwnd)) continue;
            var r = GetVisibleBounds(hwnd);
            if (r is { } b && x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom)
                return new WindowInfo(hwnd, title, GetProcessName(pid), pid);
        }
        return null;
    }

    // Janela de topo que receberia um clique no ponto, pulando as do app, ocultas e transparentes.
    public static IntPtr HitTestTopLevel(int x, int y, Func<IntPtr, bool>? exclude = null)
    {
        exclude ??= IsOwnWindow;
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
            var ex = GetWindowLong(h, GWL_EXSTYLE);
            if ((ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_LAYERED) != 0) return true;
            if (exclude(h)) return true;
            if (GetVisibleBounds(h) is not { } r || x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    // Limites visíveis (sem a borda invisível de redimensionamento do Windows 10/11).
    public static RECT? GetVisibleBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0 && r.Width > 0)
            return r;
        return GetWindowRect(hwnd, out r) ? r : null;
    }

    // Área cliente em coordenadas de tela.
    public static RECT? GetClientScreenRect(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rc)) return null;
        var p = new POINT(0, 0);
        ClientToScreen(hwnd, ref p);
        return new RECT { Left = p.X, Top = p.Y, Right = p.X + rc.Width, Bottom = p.Y + rc.Height };
    }
}
