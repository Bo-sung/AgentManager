using AgentManager.Core.Models;

namespace AgentManager.Core.Engines;

/// <summary>Legacy per-engine values read from the OLD settings.json schema (before the per-engine files),
/// to fold into the new <c>engines/&lt;id&gt;.json</c>. All fields optional — only present values override the default.</summary>
public sealed record LegacyEngineData(
    string? Path = null,
    string? AuthMode = null,
    string? ApiKeyEnc = null,
    bool? AutoApiOnLimit = null,
    string? DefaultModel = null,
    IReadOnlyList<string>? PreferredModels = null,
    string? SkillDir = null,
    bool? Disabled = null);

/// <summary>
/// One-time migration that folds the legacy scattered settings (per-engine path/auth/default/preferred/skillDir/
/// disabled from settings.json + the model catalog from <c>models.json</c>) into per-engine <see cref="EngineConfig"/>
/// seeds. Lossless: a present legacy value wins over the default; models come from the live <see cref="ModelCatalog"/>
/// with per-model efforts (stored only when they differ from the engine default, so files stay tidy) and preferred
/// flags applied. Only built-in engines are migrated (custom engines are new).
/// </summary>
public static class EngineConfigMigration
{
    public static IReadOnlyList<EngineConfig> Apply(
        IReadOnlyList<EngineConfig> defaults,
        IReadOnlyDictionary<string, LegacyEngineData> legacy,
        ModelCatalog catalog)
    {
        var result = new List<EngineConfig>(defaults.Count);
        foreach (var d in defaults)
        {
            legacy.TryGetValue(d.Id, out var L);
            var preferred = L?.PreferredModels is { } pm
                ? new HashSet<string>(pm, StringComparer.OrdinalIgnoreCase)
                : [];

            // Models from the live catalog (user's models.json). Store per-model efforts only when they differ
            // from the engine's default effort list (else null → inherit, keeping the file clean).
            var engineDefaults = d.DefaultEfforts ?? [];
            var catModels = catalog.ModelsFor(d.Id);
            var models = catModels.Count > 0
                ? catModels.Select(mid =>
                    {
                        var eff = catalog.EffortsFor(d.Id, mid);
                        var perModel = eff.Count > 0 && !eff.SequenceEqual(engineDefaults, StringComparer.OrdinalIgnoreCase)
                            ? eff : null;
                        return new EngineModelConfig(mid, perModel, catalog.DefaultEffortFor(d.Id, mid), preferred.Contains(mid));
                    }).ToArray()
                // No catalog entry → keep the default models but still apply preferred flags.
                : d.ModelList.Select(m => m with { Preferred = preferred.Contains(m.Id) }).ToArray();

            result.Add(d with
            {
                Path = L?.Path ?? d.Path,
                Auth = new EngineAuthConfig(
                    Mode: string.IsNullOrWhiteSpace(L?.AuthMode) ? d.AuthOrDefault.Mode : L!.AuthMode!,
                    ApiKeyEnc: L?.ApiKeyEnc ?? d.AuthOrDefault.ApiKeyEnc,
                    AutoApiOnLimit: L?.AutoApiOnLimit ?? d.AuthOrDefault.AutoApiOnLimit),
                DefaultModel = string.IsNullOrWhiteSpace(L?.DefaultModel) ? d.DefaultModel : L!.DefaultModel,
                SkillDir = string.IsNullOrWhiteSpace(L?.SkillDir) ? d.SkillDir : L!.SkillDir!,
                Enabled = L?.Disabled == true ? false : d.Enabled,
                Models = models,
            });
        }
        return result;
    }
}
