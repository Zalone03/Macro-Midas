using System.Collections.Concurrent;
using MacroMidas.Input;
using static MacroMidas.Native.Win32;

namespace MacroMidas.Hooks;

// Atalhos globais via RegisterHotKey, numa thread própria com fila de mensagens.
public sealed class HotkeyService : IDisposable
{
    public sealed record Binding(string Name, string Chord, Action Callback);

    private const uint WmApply = WM_APP + 1;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ConcurrentQueue<(IReadOnlyList<Binding> Bindings, TaskCompletionSource<IReadOnlyList<string>> Done)> _pending = new();
    private readonly Dictionary<int, Binding> _active = [];
    private readonly Action<Action> _dispatch;
    private uint _threadId;
    private volatile bool _disposed;

    // Quando true, os atalhos são ignorados (ex.: enquanto o usuário captura uma tecla).
    public bool Suspended { get; set; }

    public HotkeyService(Action<Action> dispatch)
    {
        _dispatch = dispatch;
        _thread = new Thread(Loop) { IsBackground = true, Name = "MacroMidas.Hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    // Troca todos os atalhos.
    public Task<IReadOnlyList<string>> SetBindingsAsync(IReadOnlyList<Binding> bindings)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue((bindings, tcs));
        PostThreadMessageW(_threadId, WmApply, IntPtr.Zero, IntPtr.Zero);
        return tcs.Task;
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessageW(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
        _ready.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY)
            {
                var id = msg.wParam.ToInt32();
                if (!Suspended && _active.TryGetValue(id, out var b)) _dispatch(b.Callback);
            }
            else if (msg.message == WmApply)
            {
                while (_pending.TryDequeue(out var job)) job.Done.TrySetResult(Apply(job.Bindings));
            }
        }

        foreach (var id in _active.Keys) UnregisterHotKey(IntPtr.Zero, id);
        _active.Clear();
    }

    private IReadOnlyList<string> Apply(IReadOnlyList<Binding> bindings)
    {
        foreach (var id in _active.Keys) UnregisterHotKey(IntPtr.Zero, id);
        _active.Clear();

        var failed = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextId = 1;
        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Chord)) continue;
            if (!VirtualKeys.TryParseChord(b.Chord, out var chord, out _)) { failed.Add($"{b.Name} ({b.Chord}: inválido)"); continue; }
            var key = VirtualKeys.Format(chord);
            if (!used.Add(key)) { failed.Add($"{b.Name} ({key}: repetido)"); continue; }
            var id = nextId++;
            if (RegisterHotKey(IntPtr.Zero, id, chord.HotkeyModifiers | MOD_NOREPEAT, chord.Vk))
                _active[id] = b;
            else
                failed.Add($"{b.Name} ({key}: em uso por outro programa)");
        }
        return failed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
