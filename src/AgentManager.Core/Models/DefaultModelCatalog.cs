using AgentManager.Core.Agents;

namespace AgentManager.Core.Models;

/// <summary>
/// Seed defaults for <see cref="ModelCatalog"/> — mirrors today's hardcoded model lists (from
/// <see cref="EngineRegistry.All"/>) and per-engine/per-model reasoning options, so a fresh
/// <c>models.json</c> reproduces the current behavior exactly. Users then edit the file to add models
/// or correct per-model efforts (e.g. gx gpt-5.6-luna's <c>max</c>, gpt-5.5's xhigh default) with no code change.
/// </summary>
public static class DefaultModelCatalog
{
    // Engine-level fallback effort lists (used by models that don't specify their own).
    // Canonical effort tokens the adapters pass (cc --effort / gx model_reasoning_effort / pi --thinking).
    private static readonly string[] CcEfforts = ["default", "low", "medium", "high", "xhigh", "max"];
    private static readonly string[] CcEffortsUltra = ["default", "low", "medium", "high", "xhigh", "max", "ultracode"];
    private static readonly string[] GxEfforts = ["none", "minimal", "low", "medium", "high", "xhigh"];
    private static readonly string[] PiEfforts = ["default", "off", "minimal", "low", "medium", "high", "xhigh"];

    public static IReadOnlyDictionary<string, EngineCatalog> Build()
    {
        var result = new Dictionary<string, EngineCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in EngineRegistry.All)
        {
            result[e.Id] = e.Id switch
            {
                "cc" => new EngineCatalog(CcEfforts, [.. e.Models.Select(m => new ModelEntry(
                            m,
                            CcSupportsUltracode(m) ? CcEffortsUltra : CcEfforts,
                            m.Contains("opus", StringComparison.OrdinalIgnoreCase) ? "medium" : "default"))]),
                "gx" => new EngineCatalog(GxEfforts, [.. e.Models.Select(m => new ModelEntry(m, GxEfforts, "medium"))]),
                "pi" => new EngineCatalog(PiEfforts, [.. e.Models.Select(m => new ModelEntry(m, PiEfforts, "default"))]),
                "agy" => new EngineCatalog([], [.. e.Models.Select(m => new ModelEntry(m))]), // agy: effort baked into the label
                _ => new EngineCatalog([], [.. e.Models.Select(m => new ModelEntry(m))]),
            };
        }
        return result;
    }

    /// <summary>Mirror of the cc ultracode gate: aliases + current full names support it; haiku (no effort)
    /// and the two known non-xhigh models (Opus 4.6 · Sonnet 4.6) do not; unknown/newer default to true.</summary>
    private static bool CcSupportsUltracode(string model)
    {
        var m = model.ToLowerInvariant();
        if (m.Contains("haiku")) return false;
        if (m.Contains("opus-4-6") || m.Contains("sonnet-4-6")) return false;
        return true;
    }
}
