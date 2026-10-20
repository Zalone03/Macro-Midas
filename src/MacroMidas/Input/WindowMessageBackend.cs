using System.Runtime.InteropServices;
using MacroMidas.Models;
using MacroMidas.Native;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Input;

// Base do modo Lock: mouse e teclado virtuais que mandam mensagens direto para as janelas.
public abstract class MessageBackendBase : InputBackendBase
{
    // Posição do cursor virtual, nas coordenadas do fluxo.
    protected int VX, VY;

    // Janela de nível superior e ponto de tela correspondentes ao cursor virtual.
    protected abstract (IntPtr Root, POINT Screen) Locate();

    // Janela que recebe o teclado.
    protected abstract IntPtr KeyboardRoot();

    public override (int X, int Y) GetPosition() => (VX, VY);

    public override void MoveTo(int x, int y)
    {
        VX = x;
        VY = y;
        var (child, cx, cy) = Resolve();
        if (child != IntPtr.Zero) PostMessageW(child, WM_MOUSEMOVE, new IntPtr(MkFlags()), MakeLParam(cx, cy));
    }

    protected override void RawButton(MouseButtonKind button, bool down, bool doubleClick)
    {
        var (child, cx, cy) = Resolve();
        if (child == IntPtr.Zero) return;
        var (downMsg, upMsg, dblMsg, mk, xb) = button switch
        {
            MouseButtonKind.Right => (WM_RBUTTONDOWN, WM_RBUTTONUP, WM_RBUTTONDBLCLK, MK_RBUTTON, 0),
            MouseButtonKind.Middle => (WM_MBUTTONDOWN, WM_MBUTTONUP, WM_MBUTTONDBLCLK, MK_MBUTTON, 0),
            MouseButtonKind.X1 => (WM_XBUTTONDOWN, WM_XBUTTONUP, WM_XBUTTONDBLCLK, MK_XBUTTON1, (int)XBUTTON1),
            MouseButtonKind.X2 => (WM_XBUTTONDOWN, WM_XBUTTONUP, WM_XBUTTONDBLCLK, MK_XBUTTON2, (int)XBUTTON2),
            _ => (WM_LBUTTONDOWN, WM_LBUTTONUP, WM_LBUTTONDBLCLK, MK_LBUTTON, 0),
        };

        var flags = MkFlags();
        flags = down ? flags | mk : flags & ~mk;
        var msg = down ? (doubleClick ? dblMsg : downMsg) : upMsg;
        PostMessageW(child, msg, MakeWParam(flags, xb), MakeLParam(cx, cy));
    }

    public override void Wheel(ScrollDirection direction)
    {
        var (root, screen) = Locate();
        var (child, _, _) = Resolve(root, screen);
        if (child == IntPtr.Zero) return;
        var delta = direction is ScrollDirection.Up or ScrollDirection.Right ? 120 : -120;
        var msg = direction is ScrollDirection.Up or ScrollDirection.Down ? WM_MOUSEWHEEL : WM_MOUSEHWHEEL;
        PostMessageW(child, msg, MakeWParam(MkFlags(), delta), MakeLParam(screen.X, screen.Y));
    }

    protected override void RawKey(ushort vk, bool down)
    {
        var target = FocusTarget(KeyboardRoot());
        if (target == IntPtr.Zero) return;
        var scan = MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
        long lp = 1 | (scan << 16);
        if (VirtualKeys.IsExtended(vk)) lp |= 1L << 24;
        var altHeld = HeldKeys.Contains(VK_MENU) || vk == VK_MENU;
        if (altHeld) lp |= 1L << 29;
        if (!down) lp |= (1L << 30) | (1L << 31);

        var sys = altHeld || vk == VK_F10;
        var msg = down ? (sys ? WM_SYSKEYDOWN : WM_KEYDOWN) : (sys ? WM_SYSKEYUP : WM_KEYUP);
        PostMessageW(target, msg, new IntPtr(vk), new IntPtr(unchecked((int)lp)));
    }

    public override void TypeChar(string grapheme)
    {
        var target = FocusTarget(KeyboardRoot());
        if (target == IntPtr.Zero) return;
        if (grapheme is "\n" or "\r\n" or "\r")
        {
            RawKey(VK_RETURN, true);
            PostMessageW(target, WM_CHAR, new IntPtr('\r'), new IntPtr(1));
            RawKey(VK_RETURN, false);
            return;
        }
        foreach (var unit in grapheme)
            PostMessageW(target, WM_CHAR, new IntPtr(unit), new IntPtr(1));
    }

    private int MkFlags()
    {
        var f = 0;
        if (HeldButtons.Contains(MouseButtonKind.Left)) f |= MK_LBUTTON;
        if (HeldButtons.Contains(MouseButtonKind.Right)) f |= MK_RBUTTON;
        if (HeldButtons.Contains(MouseButtonKind.Middle)) f |= MK_MBUTTON;
        if (HeldButtons.Contains(MouseButtonKind.X1)) f |= MK_XBUTTON1;
        if (HeldButtons.Contains(MouseButtonKind.X2)) f |= MK_XBUTTON2;
        if (HeldKeys.Contains(VK_SHIFT)) f |= MK_SHIFT;
        if (HeldKeys.Contains(VK_CONTROL)) f |= MK_CONTROL;
        return f;
    }

    private (IntPtr Hwnd, int X, int Y) Resolve()
    {
        var (root, screen) = Locate();
        return Resolve(root, screen);
    }

    // Acha a janela filha mais profunda sob o ponto e converte para as coordenadas dela.
    private static (IntPtr Hwnd, int X, int Y) Resolve(IntPtr root, POINT screen)
    {
        if (root == IntPtr.Zero) return (IntPtr.Zero, 0, 0);
        var current = root;
        for (var depth = 0; depth < 32; depth++)
        {
            var local = screen;
            ScreenToClient(current, ref local);
            var child = ChildWindowFromPointEx(current, local, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == current) break;
            current = child;
        }

        var pt = screen;
        ScreenToClient(current, ref pt);
        return (current, pt.X, pt.Y);
    }

    // Janela com foco dentro da janela alvo (ex.: a caixa de texto), mesmo em segundo plano.
    private static IntPtr FocusTarget(IntPtr root)
    {
        if (root == IntPtr.Zero) return IntPtr.Zero;
        var thread = GetWindowThreadProcessId(root, out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(thread, ref info) && info.hwndFocus != IntPtr.Zero &&
            (info.hwndFocus == root || IsChild(root, info.hwndFocus)))
            return info.hwndFocus;
        return root;
    }
}

// Lock numa janela escolhida: coordenadas relativas à área da janela.
public sealed class WindowMessageBackend : MessageBackendBase
{
    private readonly IntPtr _hwnd;

    public WindowMessageBackend(IntPtr hwnd)
    {
        _hwnd = hwnd;
        GetCursorPos(out var p);
        ScreenToClient(hwnd, ref p);
        if (GetClientRect(hwnd, out var rc))
        {
            var inside = p.X >= 0 && p.Y >= 0 && p.X < rc.Width && p.Y < rc.Height;
            VX = inside ? p.X : rc.Width / 2;
            VY = inside ? p.Y : rc.Height / 2;
        }
    }

    public override string Name => "Lock (só nessa janela)";

    protected override (IntPtr Root, POINT Screen) Locate()
    {
        var screen = new POINT(VX, VY);
        ClientToScreen(_hwnd, ref screen);
        return (_hwnd, screen);
    }

    protected override IntPtr KeyboardRoot() => _hwnd;
}

// Lock na tela inteira: cada clique vai para a janela sob o ponto.
public sealed class ScreenMessageBackend : MessageBackendBase
{
    private readonly Func<IntPtr, bool>? _exclude;
    private bool _started;
    private IntPtr _lastRoot;

    public ScreenMessageBackend(Func<IntPtr, bool>? exclude = null)
    {
        _exclude = exclude;
        GetCursorPos(out var p);
        if (!WindowFinder.IsOwnWindow(WindowFromPoint(p))) Snapshot(p);
    }

    public override string Name => "Lock (sem mexer no seu mouse)";

    private void Snapshot(POINT p)
    {
        _started = true;
        VX = p.X;
        VY = p.Y;
    }

    private void EnsureStarted()
    {
        if (_started) return;
        GetCursorPos(out var p);
        Snapshot(p);
    }

    public override (int X, int Y) GetPosition()
    {
        EnsureStarted();
        return base.GetPosition();
    }

    public override void MoveTo(int x, int y)
    {
        _started = true;
        base.MoveTo(x, y);
    }

    protected override (IntPtr Root, POINT Screen) Locate()
    {
        EnsureStarted();
        var root = WindowFinder.HitTestTopLevel(VX, VY, _exclude);
        if (root != IntPtr.Zero) _lastRoot = root;
        return (root, new POINT(VX, VY));
    }

    protected override IntPtr KeyboardRoot()
    {
        EnsureStarted();
        var root = WindowFinder.HitTestTopLevel(VX, VY, _exclude);
        return root != IntPtr.Zero ? root : _lastRoot;
    }
}
