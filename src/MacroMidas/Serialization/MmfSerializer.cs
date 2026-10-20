using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MacroMidas.Models;

namespace MacroMidas.Serialization;

// Formato próprio .mmf (Macro Midas Fluxe): JSON com cabeçalho de formato/versão.
public static class MmfSerializer
{
    public const string FormatId = "macro-midas-flow";
    public const int CurrentVersion = 1;
    public const string Extension = ".mmf";

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipComputed } },
    };

    // Textos calculados (título, resumo, ícone) não vão para o arquivo.
    private static void SkipComputed(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        if (!typeof(FlowStep).IsAssignableFrom(info.Type) && info.Type != typeof(Condition)) return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
            if (info.Properties[i].Set == null) info.Properties.RemoveAt(i);
    }

    private sealed class Envelope
    {
        public string Format { get; set; } = FormatId;
        public int Version { get; set; } = CurrentVersion;
        public string App { get; set; } = "Macro Midas";
        public Flow? Flow { get; set; }
    }

    public static string Serialize(Flow flow) => JsonSerializer.Serialize(new Envelope { Flow = flow }, Options);

    public static Flow Deserialize(string json)
    {
        Envelope? env;
        try
        {
            env = JsonSerializer.Deserialize<Envelope>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Arquivo .mmf inválido: {ex.Message}", ex);
        }

        if (env == null || env.Format != FormatId)
            throw new FormatException("Este arquivo não é um fluxo do Macro Midas (.mmf).");
        if (env.Version > CurrentVersion)
            throw new FormatException($"Fluxo criado numa versão mais nova do Macro Midas (formato v{env.Version}).");
        return env.Flow ?? throw new FormatException("O arquivo não contém um fluxo.");
    }

    public static Flow Clone(Flow flow) =>
        JsonSerializer.Deserialize<Flow>(JsonSerializer.Serialize(flow, Options), Options)!;

    public static FlowStep CloneStep(FlowStep step) =>
        JsonSerializer.Deserialize<FlowStep>(JsonSerializer.Serialize(step, Options), Options)!;

    public static string SerializeSteps(IEnumerable<FlowStep> steps) =>
        JsonSerializer.Serialize(steps.ToList(), Options);

    public static List<FlowStep> DeserializeSteps(string json) =>
        JsonSerializer.Deserialize<List<FlowStep>>(json, Options) ?? [];
}
