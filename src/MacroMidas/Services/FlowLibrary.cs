using MacroMidas.Models;
using MacroMidas.Serialization;

namespace MacroMidas.Services;

// Guarda os fluxos em %AppData%\MacroMidas\Flows\*.mmf e cuida de importar/exportar.
public sealed class FlowLibrary
{
    private readonly Dictionary<Guid, string> _lastSaved = [];

    public string PathFor(Flow flow) => Path.Combine(AppPaths.Flows, $"{flow.Id:N}{MmfSerializer.Extension}");

    public List<Flow> LoadAll()
    {
        AppPaths.Ensure();
        var flows = new List<Flow>();
        foreach (var file in Directory.EnumerateFiles(AppPaths.Flows, "*" + MmfSerializer.Extension))
        {
            try
            {
                var flow = MmfSerializer.Deserialize(File.ReadAllText(file));
                if (flows.Any(f => f.Id == flow.Id) || Path.GetFileNameWithoutExtension(file) != flow.Id.ToString("N"))
                {
                    flow.Id = Guid.NewGuid();
                    File.Delete(file);
                    Save(flow);
                }
                flows.Add(flow);
                _lastSaved[flow.Id] = MmfSerializer.Serialize(flow);
            }
            catch (Exception ex)
            {
                CrashLog.Write($"Fluxo ilegível: {file}", ex);
            }
        }
        return [.. flows.OrderBy(f => f.CreatedAt)];
    }

    // Carimba a resolução da tela principal (para avisar ao importar em outra resolução).
    public static void StampResolution(Flow flow)
    {
        var (w, h) = Native.Win32.PrimaryScreenSize();
        flow.Settings.AuthorScreenWidth = w;
        flow.Settings.AuthorScreenHeight = h;
    }

    // Salva só se algo mudou desde a última gravação.
    public bool SaveIfChanged(Flow flow)
    {
        StampResolution(flow);
        var json = MmfSerializer.Serialize(flow);
        if (_lastSaved.TryGetValue(flow.Id, out var old) && old == json) return false;
        flow.ModifiedAt = DateTime.Now;
        Save(flow);
        return true;
    }

    public void Save(Flow flow)
    {
        StampResolution(flow);
        var json = MmfSerializer.Serialize(flow);
        AppPaths.WriteAllTextAtomic(PathFor(flow), json);
        _lastSaved[flow.Id] = json;
    }

    public void Delete(Flow flow)
    {
        _lastSaved.Remove(flow.Id);
        var path = PathFor(flow);
        if (File.Exists(path)) File.Delete(path);
    }

    // Lê .mmf ou .txt (detecta pelo conteúdo).
    public static Flow Import(string path)
    {
        var text = File.ReadAllText(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var flow = text.TrimStart().StartsWith('{')
            ? MmfSerializer.Deserialize(text)
            : TxtFlowParser.Parse(text, name);
        flow.Id = Guid.NewGuid();
        flow.CreatedAt = DateTime.Now;
        flow.ModifiedAt = DateTime.Now;
        if (string.IsNullOrWhiteSpace(flow.Name)) flow.Name = name;
        return flow;
    }

    public static void Export(Flow flow, string path)
    {
        StampResolution(flow);
        var isTxt = Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, isTxt ? TxtFlowWriter.Write(flow) : MmfSerializer.Serialize(flow));
    }

    // Aviso quando um fluxo em pixels foi feito noutra resolução de tela.
    public static string? ResolutionWarning(Flow flow)
    {
        var s = flow.Settings;
        if (s.IsProportional || s.Target == TargetKind.Window) return null;
        if (s.AuthorScreenWidth <= 0 || s.AuthorScreenHeight <= 0) return null;
        var (w, h) = Native.Win32.PrimaryScreenSize();
        if (s.AuthorScreenWidth == w && s.AuthorScreenHeight == h) return null;
        return $"“{flow.Name}” foi feito em {s.AuthorScreenWidth}×{s.AuthorScreenHeight} e aqui a tela é {w}×{h}: " +
               "as posições podem precisar de ajuste. Dica: ligue “Posições proporcionais” em Opções de execução.";
    }
}
