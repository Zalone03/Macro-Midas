using MacroMidas.Models;

namespace MacroMidas.Input;

// "Mouse e teclado" usados pelo motor.
public interface IInputBackend : IDisposable
{
    string Name { get; }

    (int X, int Y) GetPosition();

    void MoveTo(int x, int y);

    void MoveBy(int dx, int dy);

    void ButtonDown(MouseButtonKind button, bool doubleClick = false);

    void ButtonUp(MouseButtonKind button);

    void Wheel(ScrollDirection direction);

    void KeyDown(ushort vk);

    void KeyUp(ushort vk);

    void TypeChar(string grapheme);

    void SuspendHeld();

    void RestoreHeld();

    void ReleaseAll();
}

// Controle comum de botões/teclas seguros para os backends.
public abstract class InputBackendBase : IInputBackend
{
    protected readonly HashSet<MouseButtonKind> HeldButtons = [];
    protected readonly List<ushort> HeldKeys = [];
    private List<MouseButtonKind>? _suspendedButtons;
    private List<ushort>? _suspendedKeys;
    private (int X, int Y)? _suspendedPos;

    // Protege o estado quando várias trilhas (bloco "Ao mesmo tempo") usam o mesmo mouse/teclado.
    protected readonly object Gate = new();

    // Marca nas entradas SendInput para o próprio app reconhecer o que ele mesmo gerou.
    public static readonly IntPtr Signature = new(0x4D4D4944);

    public abstract string Name { get; }
    public abstract (int X, int Y) GetPosition();
    public abstract void MoveTo(int x, int y);

    public virtual void MoveBy(int dx, int dy)
    {
        var (x, y) = GetPosition();
        MoveTo(x + dx, y + dy);
    }
    protected abstract void RawButton(MouseButtonKind button, bool down, bool doubleClick);
    protected abstract void RawKey(ushort vk, bool down);
    public abstract void Wheel(ScrollDirection direction);
    public abstract void TypeChar(string grapheme);

    public void ButtonDown(MouseButtonKind button, bool doubleClick = false)
    {
        lock (Gate)
        {
            RawButton(button, true, doubleClick);
            HeldButtons.Add(button);
        }
    }

    public void ButtonUp(MouseButtonKind button)
    {
        lock (Gate)
        {
            RawButton(button, false, false);
            HeldButtons.Remove(button);
        }
    }

    public void KeyDown(ushort vk)
    {
        lock (Gate)
        {
            RawKey(vk, true);
            if (!HeldKeys.Contains(vk)) HeldKeys.Add(vk);
        }
    }

    public void KeyUp(ushort vk)
    {
        lock (Gate)
        {
            RawKey(vk, false);
            HeldKeys.Remove(vk);
        }
    }

    public void SuspendHeld()
    {
        lock (Gate)
        {
            if (HeldButtons.Count == 0 && HeldKeys.Count == 0) return;
            _suspendedButtons = [.. HeldButtons];
            _suspendedKeys = [.. HeldKeys];
            _suspendedPos = GetPosition();
            ReleaseAll();
        }
    }

    public void RestoreHeld()
    {
        lock (Gate)
        {
            if (_suspendedButtons == null && _suspendedKeys == null) return;
            if (_suspendedPos is { } p) MoveTo(p.X, p.Y);
            foreach (var k in _suspendedKeys ?? []) KeyDown(k);
            foreach (var b in _suspendedButtons ?? []) ButtonDown(b);
            _suspendedButtons = null;
            _suspendedKeys = null;
            _suspendedPos = null;
        }
    }

    public void ReleaseAll()
    {
        lock (Gate)
        {
            foreach (var b in HeldButtons.ToArray()) ButtonUp(b);
            for (var i = HeldKeys.Count - 1; i >= 0; i--) KeyUp(HeldKeys[i]);
            HeldButtons.Clear();
            HeldKeys.Clear();
        }
    }

    public virtual void Dispose() => ReleaseAll();
}
