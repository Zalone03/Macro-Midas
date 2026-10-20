using System.Text;
using AvKey = Avalonia.Input.Key;
using AvMods = Avalonia.Input.KeyModifiers;

namespace MacroMidas.Input;

// Tecla principal + modificadores.
public readonly record struct KeyChord(ushort Vk, bool Ctrl = false, bool Shift = false, bool Alt = false, bool Win = false)
{
    public IEnumerable<ushort> ModifierVks()
    {
        if (Ctrl) yield return 0x11;
        if (Alt) yield return 0x12;
        if (Shift) yield return 0x10;
        if (Win) yield return 0x5B;
    }

    public uint HotkeyModifiers =>
        (Ctrl ? 0x2u : 0) | (Alt ? 0x1u : 0) | (Shift ? 0x4u : 0) | (Win ? 0x8u : 0);

    public override string ToString() => VirtualKeys.Format(this);
}

// Nomes de teclas, parsing de combinações e conversão a partir das teclas do Avalonia.
public static class VirtualKeys
{
    private static readonly Dictionary<string, ushort> NameToVk = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<ushort, string> VkToName = [];
    private static readonly List<string> Names = [];

    static VirtualKeys()
    {
        for (var c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c);
        for (var d = 0; d <= 9; d++) Add(d.ToString(), (ushort)('0' + d));
        for (var f = 1; f <= 24; f++) Add($"F{f}", (ushort)(0x6F + f));
        for (var n = 0; n <= 9; n++) Add($"Numpad{n}", (ushort)(0x60 + n));

        Add("Enter", 0x0D, "Return");
        Add("Esc", 0x1B, "Escape");
        Add("Space", 0x20, "Espaço", "Espaco");
        Add("Tab", 0x09);
        Add("Backspace", 0x08, "Back");
        Add("Delete", 0x2E, "Del");
        Add("Insert", 0x2D, "Ins");
        Add("Home", 0x24);
        Add("End", 0x23);
        Add("PageUp", 0x21, "PgUp");
        Add("PageDown", 0x22, "PgDn");
        Add("Left", 0x25, "Esquerda");
        Add("Up", 0x26, "Cima");
        Add("Right", 0x27, "Direita");
        Add("Down", 0x28, "Baixo");
        Add("CapsLock", 0x14);
        Add("NumLock", 0x90);
        Add("ScrollLock", 0x91);
        Add("PrintScreen", 0x2C, "PrtSc");
        Add("Pause", 0x13);
        Add("Apps", 0x5D, "Menu");

        Add("Ctrl", 0x11, "Control");
        Add("Shift", 0x10);
        Add("Alt", 0x12);
        Add("Win", 0x5B, "LWin", "Windows");
        Add("RWin", 0x5C);
        Add("LCtrl", 0xA2);
        Add("RCtrl", 0xA3);
        Add("LShift", 0xA0);
        Add("RShift", 0xA1);
        Add("LAlt", 0xA4);
        Add("RAlt", 0xA5, "AltGr");

        Add("NumpadMultiply", 0x6A);
        Add("NumpadAdd", 0x6B);
        Add("NumpadSeparator", 0x6C);
        Add("NumpadSubtract", 0x6D);
        Add("NumpadDecimal", 0x6E);
        Add("NumpadDivide", 0x6F);

        Add("Oem1", 0xBA);
        Add("OemPlus", 0xBB);
        Add("OemComma", 0xBC);
        Add("OemMinus", 0xBD);
        Add("OemPeriod", 0xBE);
        Add("Oem2", 0xBF);
        Add("Oem3", 0xC0);
        Add("AbntC1", 0xC1);
        Add("AbntC2", 0xC2);
        Add("Oem4", 0xDB);
        Add("Oem5", 0xDC);
        Add("Oem6", 0xDD);
        Add("Oem7", 0xDE);
        Add("Oem8", 0xDF);
        Add("Oem102", 0xE2);

        Add("VolumeMute", 0xAD);
        Add("VolumeDown", 0xAE);
        Add("VolumeUp", 0xAF);
        Add("MediaNext", 0xB0);
        Add("MediaPrev", 0xB1);
        Add("MediaStop", 0xB2);
        Add("MediaPlayPause", 0xB3);
    }

    private static void Add(string name, ushort vk, params string[] aliases)
    {
        NameToVk[name] = vk;
        VkToName.TryAdd(vk, name);
        Names.Add(name);
        foreach (var a in aliases) NameToVk[a] = vk;
    }

    private static void Add(string name, char c) => Add(name, (ushort)c);

    public static IReadOnlyList<string> AllNames => Names;

    public static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    // Teclas que precisam de KEYEVENTF_EXTENDEDKEY.
    public static bool IsExtended(ushort vk) => vk is
        0xA3 or 0xA5 or 0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22 or 0x25 or 0x26 or 0x27 or 0x28 or
        0x90 or 0x03 or 0x2C or 0x6F or 0x5B or 0x5C or 0x5D or (>= 0xAD and <= 0xB3);

    public static string NameOf(ushort vk) => VkToName.TryGetValue(vk, out var n) ? n : $"VK{vk:X2}";

    public static bool TryParseKey(string token, out ushort vk)
    {
        token = token.Trim();
        if (NameToVk.TryGetValue(token, out vk)) return true;
        if (token.Length > 2 && token.StartsWith("VK", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(token.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out vk) && vk is > 0 and < 0xFF)
            return true;
        vk = 0;
        return false;
    }

    // Lê "Ctrl+Shift+S", "Alt+F4", "Enter", "Shift" (modificador sozinho vira a tecla principal).
    public static bool TryParseChord(string? text, out KeyChord chord, out string? error)
    {
        chord = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Nenhuma tecla definida"; return false; }

        var raw = text.Trim();
        var tokens = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (ch == '+' && sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());

        bool ctrl = false, shift = false, alt = false, win = false;
        ushort main = 0;
        string? mainToken = null;
        foreach (var t in tokens.Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var lower = t.ToLowerInvariant();
            if (mainToken == null && lower is "ctrl" or "control") { ctrl = true; continue; }
            if (mainToken == null && lower is "shift") { shift = true; continue; }
            if (mainToken == null && lower is "alt") { alt = true; continue; }
            if (mainToken == null && lower is "win" or "windows" or "meta" or "cmd") { win = true; continue; }
            if (mainToken != null) { error = $"Mais de uma tecla principal: “{mainToken}” e “{t}”"; return false; }
            var token = t == "+" ? "OemPlus" : t;
            if (!TryParseKey(token, out main)) { error = $"Tecla desconhecida: “{t}”"; return false; }
            mainToken = t;
        }

        if (mainToken == null)
        {
            if (win) { main = 0x5B; win = false; }
            else if (alt) { main = 0x12; alt = false; }
            else if (shift) { main = 0x10; shift = false; }
            else if (ctrl) { main = 0x11; ctrl = false; }
            else { error = "Nenhuma tecla definida"; return false; }
        }

        chord = new KeyChord(main, ctrl, shift, alt, win);
        return true;
    }

    public static string Format(KeyChord c)
    {
        var parts = new List<string>(5);
        if (c.Ctrl) parts.Add("Ctrl");
        if (c.Shift) parts.Add("Shift");
        if (c.Alt) parts.Add("Alt");
        if (c.Win) parts.Add("Win");
        parts.Add(NameOf(c.Vk));
        return string.Join("+", parts);
    }

    // Normaliza o texto digitado pelo usuário ("ctrl + s" → "Ctrl+S").
    public static string? Normalize(string? text) => TryParseChord(text, out var c, out _) ? Format(c) : null;

    // Converte a tecla pressionada numa janela Avalonia para o formato de combinação.
    public static string? ChordFromAvalonia(AvKey key, AvMods mods)
    {
        var vk = FromAvaloniaKey(key);
        if (vk is null) return null;
        var isMod = IsModifier(vk.Value);
        var chord = new KeyChord(
            NormalizeModifierVk(vk.Value),
            Ctrl: !isMod && mods.HasFlag(AvMods.Control),
            Shift: !isMod && mods.HasFlag(AvMods.Shift),
            Alt: !isMod && mods.HasFlag(AvMods.Alt),
            Win: !isMod && mods.HasFlag(AvMods.Meta));
        return Format(chord);
    }

    private static ushort NormalizeModifierVk(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        0x5C => 0x5B,
        _ => vk
    };

    public static ushort? FromAvaloniaKey(AvKey key)
    {
        if (key is >= AvKey.A and <= AvKey.Z) return (ushort)(0x41 + (key - AvKey.A));
        if (key is >= AvKey.D0 and <= AvKey.D9) return (ushort)(0x30 + (key - AvKey.D0));
        if (key is >= AvKey.NumPad0 and <= AvKey.NumPad9) return (ushort)(0x60 + (key - AvKey.NumPad0));
        if (key is >= AvKey.F1 and <= AvKey.F24) return (ushort)(0x70 + (key - AvKey.F1));
        return AvaloniaMap.TryGetValue(key, out var vk) ? vk : null;
    }

    private static readonly Dictionary<AvKey, ushort> AvaloniaMap = new()
    {
        [AvKey.Enter] = 0x0D, [AvKey.Escape] = 0x1B, [AvKey.Space] = 0x20, [AvKey.Tab] = 0x09,
        [AvKey.Back] = 0x08, [AvKey.Delete] = 0x2E, [AvKey.Insert] = 0x2D, [AvKey.Home] = 0x24,
        [AvKey.End] = 0x23, [AvKey.PageUp] = 0x21, [AvKey.PageDown] = 0x22, [AvKey.Left] = 0x25,
        [AvKey.Up] = 0x26, [AvKey.Right] = 0x27, [AvKey.Down] = 0x28, [AvKey.CapsLock] = 0x14,
        [AvKey.NumLock] = 0x90, [AvKey.Scroll] = 0x91, [AvKey.PrintScreen] = 0x2C, [AvKey.Pause] = 0x13,
        [AvKey.Apps] = 0x5D, [AvKey.LWin] = 0x5B, [AvKey.RWin] = 0x5C,
        [AvKey.LeftShift] = 0xA0, [AvKey.RightShift] = 0xA1, [AvKey.LeftCtrl] = 0xA2, [AvKey.RightCtrl] = 0xA3,
        [AvKey.LeftAlt] = 0xA4, [AvKey.RightAlt] = 0xA5,
        [AvKey.Multiply] = 0x6A, [AvKey.Add] = 0x6B, [AvKey.Separator] = 0x6C, [AvKey.Subtract] = 0x6D,
        [AvKey.Decimal] = 0x6E, [AvKey.Divide] = 0x6F,
        [AvKey.OemSemicolon] = 0xBA, [AvKey.OemPlus] = 0xBB, [AvKey.OemComma] = 0xBC, [AvKey.OemMinus] = 0xBD,
        [AvKey.OemPeriod] = 0xBE, [AvKey.OemQuestion] = 0xBF, [AvKey.OemTilde] = 0xC0, [AvKey.AbntC1] = 0xC1,
        [AvKey.AbntC2] = 0xC2, [AvKey.OemOpenBrackets] = 0xDB, [AvKey.OemPipe] = 0xDC,
        [AvKey.OemCloseBrackets] = 0xDD, [AvKey.OemQuotes] = 0xDE, [AvKey.Oem8] = 0xDF, [AvKey.OemBackslash] = 0xE2,
        [AvKey.VolumeMute] = 0xAD, [AvKey.VolumeDown] = 0xAE, [AvKey.VolumeUp] = 0xAF,
        [AvKey.MediaNextTrack] = 0xB0, [AvKey.MediaPreviousTrack] = 0xB1, [AvKey.MediaStop] = 0xB2,
        [AvKey.MediaPlayPause] = 0xB3,
    };
}
