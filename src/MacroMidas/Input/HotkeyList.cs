namespace MacroMidas.Input;

// Várias teclas de atalho para uma ação, guardadas no mesmo texto: "F6, Ctrl+Alt+1".
public static class HotkeyList
{
    public static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> chords) =>
        string.Join(", ", chords.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase));

    // A primeira tecla (a que aparece nos menus e avisos).
    public static string First(string? value) => Split(value) is { Count: > 0 } list ? list[0] : "";

    // "F6 ou Ctrl+Alt+1".
    public static string Display(string? value) => string.Join(" ou ", Split(value));
}
