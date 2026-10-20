namespace MacroMidas.Services;

public static class AppPaths
{
    public static string Root { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MacroMidas");

    // Troca a pasta de dados (usado pelos testes para não tocar nos fluxos reais).
    public static void UseRoot(string path) => Root = path;

    public static string Flows => Path.Combine(Root, "Flows");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Flows);
        Directory.CreateDirectory(Logs);
    }

    // Grava de forma atômica (arquivo temporário + troca), para não corromper em caso de queda.
    public static void WriteAllTextAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}

public static class CrashLog
{
    private static readonly object Gate = new();

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("UnobservedTask", e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(string kind, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.Ensure();
                File.AppendAllText(Path.Combine(AppPaths.Logs, "erros.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {ex}\n\n");
            }
        }
        catch
        {
        }
    }
}
