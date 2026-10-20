using System.Diagnostics;
using System.Runtime.InteropServices;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Hooks;

// Movimento físico do mouse em deslocamentos (Raw Input).
public sealed class RawMouseInput(Stopwatch clock) : IDisposable
{
    private Thread? _thread;
    private uint _threadId;

    // Tempo (ms do relógio compartilhado), deslocamento X e Y.
    public event Action<long, int, int>? Moved;

    public bool Start()
    {
        if (_thread != null) return true;
        using var ready = new ManualResetEventSlim();
        var ok = false;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var hwnd = CreateWindowExW(0, "Message", "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            var size = (uint)Marshal.SizeOf<RAWINPUTDEVICE>();
            ok = hwnd != IntPtr.Zero && RegisterRawInputDevices(
                [new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }], 1, size);
            ready.Set();
            if (!ok)
            {
                if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
                return;
            }

            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message == WM_INPUT) Read(msg.lParam, buffer);
                    DispatchMessageW(ref msg);
                }
            }
            finally
            {
                RegisterRawInputDevices(
                    [new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 2, dwFlags = RIDEV_REMOVE, hwndTarget = IntPtr.Zero }], 1, size);
                DestroyWindow(hwnd);
                Marshal.FreeHGlobal(buffer);
            }
        })
        {
            IsBackground = true,
            Name = "MacroMidas.RawMouse",
            Priority = ThreadPriority.Highest
        };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        return ok;
    }

    private void Read(IntPtr handle, IntPtr buffer)
    {
        uint size = 256;
        var header = (uint)(8 + 2 * IntPtr.Size);
        if (GetRawInputData(handle, RID_INPUT, buffer, ref size, header) == uint.MaxValue) return;
        if ((uint)Marshal.ReadInt32(buffer, 0) != RIM_TYPEMOUSE) return;
        if (Marshal.ReadIntPtr(buffer, 8) == IntPtr.Zero) return;

        var m = (int)header;
        var flags = (ushort)Marshal.ReadInt16(buffer, m);
        if ((flags & MOUSE_MOVE_ABSOLUTE) != 0) return;
        var dx = Marshal.ReadInt32(buffer, m + 12);
        var dy = Marshal.ReadInt32(buffer, m + 16);
        if (dx == 0 && dy == 0) return;
        try
        {
            Moved?.Invoke(clock.ElapsedMilliseconds, dx, dy);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_thread == null) return;
        PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }
}
