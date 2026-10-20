using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Diagnostics;

// Simula um clique real do Windows num controle do próprio app, sem mexer no cursor.
internal static class UiProbe
{
    public sealed record ClickResult(int HitTest, int MouseActivate, PixelPoint Screen)
    {
        public bool HitsClient => HitTest == HTCLIENT;
        public bool NotEaten => MouseActivate is not (MA_ACTIVATEANDEAT or MA_NOACTIVATEANDEAT);
        public override string ToString() => $"hittest={HitTest} mouseactivate={MouseActivate} tela=({Screen.X},{Screen.Y})";
    }

    public static IntPtr HwndOf(TopLevel top) => top.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    public static async Task<ClickResult> ClickAsync(Control control, int settleMs = 150)
    {
        var top = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("controle fora de janela");
        var hwnd = HwndOf(top);
        var center = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        var lScreen = MakeLParam(center.X, center.Y);

        var hit = (int)SendMessageW(hwnd, WM_NCHITTEST, IntPtr.Zero, lScreen);
        var act = (int)SendMessageW(hwnd, WM_MOUSEACTIVATE, hwnd, MakeLParam(hit, (int)WM_LBUTTONDOWN));

        var pt = new POINT(center.X, center.Y);
        ScreenToClient(hwnd, ref pt);
        var lClient = MakeLParam(pt.X, pt.Y);
        if (hit == HTCLIENT && act is not (MA_ACTIVATEANDEAT or MA_NOACTIVATEANDEAT))
        {
            PostMessageW(hwnd, WM_MOUSEMOVE, IntPtr.Zero, lClient);
            PostMessageW(hwnd, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), lClient);
            PostMessageW(hwnd, WM_LBUTTONUP, IntPtr.Zero, lClient);
        }
        await Pump(settleMs);
        return new ClickResult(hit, act, center);
    }

    public static async Task Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
    }

    public static T? Find<T>(Visual root, Func<T, bool> match) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(match);
}
