using MacroMidas.Models;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Input;

// Entrada "real" via SendInput: controla o cursor e o teclado do sistema.
public sealed class DirectInputBackend(IntPtr targetHwnd) : InputBackendBase
{
    public override string Name => targetHwnd == IntPtr.Zero ? "Direto (SendInput)" : "Direto na janela (SendInput)";

    public override (int X, int Y) GetPosition()
    {
        GetCursorPos(out var p);
        if (targetHwnd != IntPtr.Zero) ScreenToClient(targetHwnd, ref p);
        return (p.X, p.Y);
    }

    // Movimento relativo de verdade (jogos com o cursor preso leem só o deslocamento).
    public override void MoveBy(int dx, int dy) => Send(MouseInput(dx, dy, 0, MOUSEEVENTF_MOVE));

    public override void MoveTo(int x, int y)
    {
        var p = new POINT(x, y);
        if (targetHwnd != IntPtr.Zero) ClientToScreen(targetHwnd, ref p);

        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var vh = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));

        var nx = (int)(((2L * (p.X - vx) + 1) * 65536) / (2L * vw));
        var ny = (int)(((2L * (p.Y - vy) + 1) * 65536) / (2L * vh));

        Send(MouseInput(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK));
    }

    private readonly HashSet<MouseButtonKind> _suppressed = [];

    // Nunca clicar/rolar em cima do próprio Macro Midas (barra de controle, janela principal).
    private static bool OverOwnWindow()
    {
        GetCursorPos(out var p);
        return Native.WindowFinder.IsOwnWindow(WindowFromPoint(p));
    }

    protected override void RawButton(MouseButtonKind button, bool down, bool doubleClick)
    {
        if (down)
        {
            if (OverOwnWindow())
            {
                _suppressed.Add(button);
                return;
            }
        }
        else if (_suppressed.Remove(button))
        {
            return;
        }

        uint flags;
        uint data = 0;
        switch (button)
        {
            case MouseButtonKind.Right: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
            case MouseButtonKind.Middle: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            case MouseButtonKind.X1: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON1; break;
            case MouseButtonKind.X2: flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP; data = XBUTTON2; break;
            default: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
        }
        Send(MouseInput(0, 0, data, flags));
    }

    public override void Wheel(ScrollDirection direction)
    {
        if (OverOwnWindow()) return;
        var (flags, delta) = direction switch
        {
            ScrollDirection.Up => (MOUSEEVENTF_WHEEL, 120),
            ScrollDirection.Down => (MOUSEEVENTF_WHEEL, -120),
            ScrollDirection.Right => (MOUSEEVENTF_HWHEEL, 120),
            _ => (MOUSEEVENTF_HWHEEL, -120),
        };
        Send(MouseInput(0, 0, unchecked((uint)delta), flags));
    }

    protected override void RawKey(ushort vk, bool down)
    {
        var scan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
        var flags = down ? 0u : KEYEVENTF_KEYUP;
        if (VirtualKeys.IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (scan != 0) flags |= KEYEVENTF_SCANCODE;
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = scan != 0 ? (ushort)0 : vk,
                    wScan = scan,
                    dwFlags = flags,
                    dwExtraInfo = Signature
                }
            }
        };
        Send(input);
    }

    public override void TypeChar(string grapheme)
    {
        if (grapheme is "\n" or "\r\n" or "\r")
        {
            RawKey(VK_RETURN, true);
            RawKey(VK_RETURN, false);
            return;
        }
        if (grapheme == "\t")
        {
            RawKey(VK_TAB, true);
            RawKey(VK_TAB, false);
            return;
        }

        var inputs = new INPUT[grapheme.Length * 2];
        var n = 0;
        foreach (var unit in grapheme) inputs[n++] = UnicodeInput(unit, false);
        foreach (var unit in grapheme) inputs[n++] = UnicodeInput(unit, true);
        SendInput((uint)n, inputs, INPUT.Size);
    }

    private static INPUT UnicodeInput(char unit, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = unit,
                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Signature
            }
        }
    };

    private static INPUT MouseInput(int dx, int dy, uint data, uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = Signature }
        }
    };

    private static void Send(INPUT input) => SendInput(1, [input], INPUT.Size);
}
