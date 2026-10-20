using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MacroMidas.Models;
using MacroMidas.Native;
using MacroMidas.ViewModels;
using MacroMidas.Views.Overlay;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Services;

// Mostra/esconde a borda pulsante, a moldura do Lock e a barra de controle.
public sealed class OverlayService
{
    private readonly Window _owner;
    private readonly List<PulseOverlayWindow> _pulses = [];
    private ControlBarWindow? _bar;
    private LockFrameWindow? _lock;
    private DispatcherTimer? _lockTimer;
    private DispatcherTimer? _barTimer;
    private CountdownOverlayWindow? _countdown;
    private IntPtr _lockTarget;

    public ControlBarViewModel Bar { get; }

    public OverlayService(Window owner, ControlBarViewModel bar)
    {
        _owner = owner;
        Bar = bar;
    }

    public static Color ParseColor(string? hex, string fallback = "#FFC83D") =>
        Color.TryParse(hex, out var c) ? c : Color.Parse(fallback);

    // Liga a borda pulsante em todos os monitores e a barra de controle.
    public void ShowSession(AppSettings settings, bool recording)
    {
        var o = settings.Overlay;
        HidePulses();

        if (o.Enabled)
        {
            var color = ParseColor(recording ? o.RecordColor : o.Color);
            foreach (var screen in _owner.Screens.All)
            {
                var w = new PulseOverlayWindow();
                w.Configure(color, o.Opacity, o.Thickness, o.GlowSize);
                w.PlaceOn(screen.Bounds);
                w.Show();
                w.StartPulse(o.PulseSeconds);
                _pulses.Add(w);
            }
        }

        if (o.ControlBar || recording)
        {
            Bar.RestOpacity = Math.Clamp(o.ControlBarOpacity, 0.15, 1);
            _bar ??= new ControlBarWindow { DataContext = Bar };
            _bar.Placement = o.ControlBarPlacement;
            if (!_bar.IsVisible) _bar.Show();
            _bar.ResetPosition();
            _barTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => KeepBarOnTop());
            _barTimer.Start();
        }
    }

    private void KeepBarOnTop()
    {
        if (_bar is not { IsVisible: true } bar || bar.Hwnd == IntPtr.Zero) return;
        SetWindowPos(bar.Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void HideSession()
    {
        HidePulses();
        _barTimer?.Stop();
        _bar?.Hide();
        HideLock();
    }

    // Contagem grande no meio da tela antes de gravar.
    public void ShowCountdown(int seconds, string label)
    {
        var screen = _owner.Screens.Primary ?? _owner.Screens.All.FirstOrDefault();
        if (screen == null) return;
        _countdown ??= new CountdownOverlayWindow();
        _countdown.Set(seconds, label);
        if (!_countdown.IsVisible) _countdown.Show();
        _countdown.PlaceOn(screen.Bounds);
    }

    public void HideCountdown()
    {
        _countdown?.Close();
        _countdown = null;
    }

    private void HidePulses()
    {
        foreach (var w in _pulses) w.Close();
        _pulses.Clear();
    }

    // Moldura "Lock" sobre a janela alvo, acompanhando posição e ordem Z.
    public void ShowLock(IntPtr target, AppSettings settings)
    {
        HideLock();
        var o = settings.Overlay;
        if (!o.LockFrame && !o.LockBlocksMouse) return;
        if (WindowFinder.GetVisibleBounds(target) is not { } r) return;

        _lockTarget = target;
        _lock = new LockFrameWindow();
        _lock.Configure(ParseColor(o.Color), o.LockBlocksMouse);
        _lock.Opacity = o.LockFrame ? 1 : 0.01;
        _lock.Show();
        _lock.PlaceAbove(target, ToPixelRect(r));
        if (o.LockFrame) _lock.StartPulse(o.PulseSeconds);

        _lockTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => TrackLock());
        _lockTimer.Start();
    }

    public void HideLock()
    {
        _lockTimer?.Stop();
        _lockTimer = null;
        _lock?.Close();
        _lock = null;
        _lockTarget = IntPtr.Zero;
    }

    private void TrackLock()
    {
        if (_lock == null) return;
        if (!IsWindow(_lockTarget))
        {
            HideLock();
            return;
        }

        var hidden = IsIconic(_lockTarget) || !IsWindowVisible(_lockTarget);
        if (hidden)
        {
            if (_lock.IsVisible) _lock.Hide();
            return;
        }
        if (WindowFinder.GetVisibleBounds(_lockTarget) is not { } r) return;
        if (!_lock.IsVisible) _lock.Show();
        _lock.PlaceAbove(_lockTarget, ToPixelRect(r));
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height));

    // Pré-visualização para a tela de configurações.
    public async Task PreviewAsync(AppSettings settings, TimeSpan duration)
    {
        Bar.Title = "Pré-visualização";
        Bar.Status = "É assim que fica durante a execução";
        Bar.IsRecording = false;
        Bar.IsPaused = false;
        Bar.StopPending = false;
        Bar.IsLock = false;
        ShowSession(settings, recording: false);
        await Task.Delay(duration);
        HideSession();
    }

    public void CloseAll()
    {
        HideSession();
        HideCountdown();
        _bar?.Close();
        _bar = null;
    }
}
