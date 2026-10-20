using System.Diagnostics;
using System.Runtime.InteropServices;
using MacroMidas.Input;
using MacroMidas.Models;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Hooks;

// RelMove: deslocamento do mouse com o cursor escondido (X/Y = dx/dy), vindo do Raw Input.
public enum HookEventKind { MouseMove, MouseDown, MouseUp, Wheel, KeyDown, KeyUp, RelMove }

public readonly record struct HookEvent(
    HookEventKind Kind,
    long TimeMs,
    int X,
    int Y,
    MouseButtonKind Button = MouseButtonKind.Left,
    ScrollDirection Wheel = ScrollDirection.Down,
    ushort Vk = 0,
    bool Injected = false,
    bool FromMacroMidas = false);

// Hooks globais de mouse/teclado (WH_*_LL) numa thread com fila de mensagens.
public sealed class LowLevelInputHook : IDisposable
{
    private readonly bool _mouse;
    private readonly bool _keyboard;
    private readonly Stopwatch _clock;
    private readonly HookProc _mouseProc;
    private readonly HookProc _keyboardProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook, _keyboardHook;

    public event Action<HookEvent>? Event;

    public LowLevelInputHook(bool mouse, bool keyboard, Stopwatch? clock = null)
    {
        _clock = clock ?? Stopwatch.StartNew();
        _mouse = mouse;
        _keyboard = keyboard;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    public bool Start()
    {
        if (_thread != null) return true;
        using var ready = new ManualResetEventSlim();
        var ok = false;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessageW(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            var hMod = GetModuleHandleW(null);
            if (_mouse) _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, hMod, 0);
            if (_keyboard) _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
            ok = (!_mouse || _mouseHook != IntPtr.Zero) && (!_keyboard || _keyboardHook != IntPtr.Zero);
            ready.Set();
            while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0) { }
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
            _mouseHook = _keyboardHook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "MacroMidas.InputHook",
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        return ok;
    }

    public void Stop()
    {
        if (_thread == null) return;
        PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose() => Stop();

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Event is { } handler)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var injected = (info.flags & LLMHF_INJECTED) != 0;
            var ours = info.dwExtraInfo == InputBackendBase.Signature;
            var t = _clock.ElapsedMilliseconds;
            var msg = (uint)wParam.ToInt64();
            HookEvent? ev = msg switch
            {
                WM_MOUSEMOVE => new HookEvent(HookEventKind.MouseMove, t, info.pt.X, info.pt.Y),
                WM_LBUTTONDOWN => new HookEvent(HookEventKind.MouseDown, t, info.pt.X, info.pt.Y, MouseButtonKind.Left),
                WM_LBUTTONUP => new HookEvent(HookEventKind.MouseUp, t, info.pt.X, info.pt.Y, MouseButtonKind.Left),
                WM_RBUTTONDOWN => new HookEvent(HookEventKind.MouseDown, t, info.pt.X, info.pt.Y, MouseButtonKind.Right),
                WM_RBUTTONUP => new HookEvent(HookEventKind.MouseUp, t, info.pt.X, info.pt.Y, MouseButtonKind.Right),
                WM_MBUTTONDOWN => new HookEvent(HookEventKind.MouseDown, t, info.pt.X, info.pt.Y, MouseButtonKind.Middle),
                WM_MBUTTONUP => new HookEvent(HookEventKind.MouseUp, t, info.pt.X, info.pt.Y, MouseButtonKind.Middle),
                WM_XBUTTONDOWN => new HookEvent(HookEventKind.MouseDown, t, info.pt.X, info.pt.Y, XButton(info.mouseData)),
                WM_XBUTTONUP => new HookEvent(HookEventKind.MouseUp, t, info.pt.X, info.pt.Y, XButton(info.mouseData)),
                WM_MOUSEWHEEL => new HookEvent(HookEventKind.Wheel, t, info.pt.X, info.pt.Y,
                    Wheel: (short)(info.mouseData >> 16) > 0 ? ScrollDirection.Up : ScrollDirection.Down),
                WM_MOUSEHWHEEL => new HookEvent(HookEventKind.Wheel, t, info.pt.X, info.pt.Y,
                    Wheel: (short)(info.mouseData >> 16) > 0 ? ScrollDirection.Right : ScrollDirection.Left),
                _ => null
            };
            if (ev is { } e)
            {
                try { handler(e with { Injected = injected, FromMacroMidas = ours }); }
                catch { }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Event is { } handler)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (uint)wParam.ToInt64();
            var kind = msg is WM_KEYDOWN or WM_SYSKEYDOWN ? HookEventKind.KeyDown : HookEventKind.KeyUp;
            var injected = (info.flags & LLKHF_INJECTED) != 0;
            var ours = info.dwExtraInfo == InputBackendBase.Signature;
            try
            {
                handler(new HookEvent(kind, _clock.ElapsedMilliseconds, 0, 0, Vk: (ushort)info.vkCode,
                    Injected: injected, FromMacroMidas: ours));
            }
            catch { }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static MouseButtonKind XButton(uint mouseData) =>
        (mouseData >> 16) == XBUTTON2 ? MouseButtonKind.X2 : MouseButtonKind.X1;
}
