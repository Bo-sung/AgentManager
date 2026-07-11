using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentManager.Core.Models;

/// <summary>One model in the catalog: its id plus (optional) per-model reasoning options and default.
/// Per-model efforts are first-class — reasoning levels genuinely differ per model (e.g. gx gpt-5.6-luna
/// has <c>max</c> while gpt-5.5 does not; pi thinking varies per model; cc ultracode is model-gated).
/// When <see cref="Efforts"/> is null the model inherits the engine's <see cref="EngineCatalog.DefaultEfforts"/>.</summary>
public sealed record ModelEntry(string Id, IReadOnlyList<string>? Efforts = null, string? DefaultEffort = null);

/// <summary>One engine's catalog: the model list plus an engine-level fallback effort list
/// (used only by models that omit their own <see cref="ModelEntry.Efforts"/>). Empty DefaultEfforts
/// = the engine has no reasoning dimension (e.g. agy — effort is baked into the model label).</summary>
public sealed record EngineCatalog(IReadOnlyList<string> DefaultEfforts, IReadOnlyList<ModelEntry> Models);

/// <summary>
/// User-editable per-engine model catalog persisted as <c>%LOCALAPPDATA%/AgentManager/models.json</c>.
/// It is the single source for the model lists shown in the composer/New-Agent/settings pickers AND for
/// the per-model reasoning (effort) options — replacing the previously hardcoded lists so a user can add
/// a model (or fix its efforts) by editing the file, with no code change. Live catalog queries (pi
/// <c>--list-models</c>, agy <c>agy models</c>) refresh the queried engine's model list via
/// <see cref="UpdateFromQuery"/>. The user's SELECTED (preferred) models stay in settings.json — this
/// file only holds the AVAILABLE catalog. Robust: a missing/corrupt file is re-seeded from defaults.
/// </summary>
public sealed class ModelCatalog
{
    private readonly Dictionary<string, EngineCatalog> _engines;
    private readonly string _path;

    private ModelCatalog(string path, Dictionary<string, EngineCatalog> engines)
    {
        _path = path;
        _engines = engines;
    }

    /// <summary>Default catalog path under LocalApplicationData (next to settings.json / state.json).</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "models.json");

    /// <summary>Load the catalog from <paramref name="path"/>, seeding+writing the file from
    /// <paramref name="defaults"/> when it is missing or unparseable. Never throws — falls back to defaults.</summary>
    public static ModelCatalog Load(IReadOnlyDictionary<string, EngineCatalog> defaults, string? path = null)
    {
        path ??= DefaultPath;
        var engines = new Dictionary<string, EngineCatalog>(defaults, StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path))
            {
                var parsed = Parse(File.ReadAllText(path));
                if (parsed.Count > 0)
                {
                    // File is authoritative for engines it defines; defaults fill in any it omits.
                    foreach (var (id, cat) in parsed) engines[id] = cat;
                    return new ModelCatalog(path, engines);
                }
            }
        }
        catch { /* unreadable/corrupt → seed from defaults below */ }

        var store = new ModelCatalog(path, engines);
        try { store.Save(); } catch { /* best-effort seed write */ }
        return store;
    }

    /// <summary>Model ids for an engine (empty if unknown).</summary>
    public IReadOnlyList<string> ModelsFor(string engineId) =>
        _engines.TryGetValue(engineId, out var cat) ? cat.Models.Select(m => m.Id).ToArray() : [];

    /// <summary>Reasoning/effort options for a specific model: the model's own list if present, else the
    /// engine's <see cref="EngineCatalog.DefaultEfforts"/>, else empty (no reasoning dimension).</summary>
    public IReadOnlyList<string> EffortsFor(string engineId, string? modelId)
    {
        if (!_engines.TryGetValue(engineId, out var cat)) return [];
        var m = Find(cat, modelId);
        return m?.Efforts ?? cat.DefaultEfforts;
    }

    /// <summary>The model's default reasoning level (the picker's "(default)"), or null if unspecified.</summary>
    public string? DefaultEffortFor(string engineId, string? modelId)
    {
        if (!_engines.TryGetValue(engineId, out var cat)) return null;
        return Find(cat, modelId)?.DefaultEffort;
    }

    /// <summary>Whether an engine has any reasoning dimension at all (false ⇒ hide the effort picker, e.g. agy).</summary>
    public bool HasEfforts(string engineId) =>
        _engines.TryGetValue(engineId, out var cat) && cat.DefaultEfforts.Count > 0;

    /// <summary>Replace an engine's model list from a live query (pi/agy). Existing per-model efforts/default
    /// are PRESERVED for ids that survive; brand-new ids get null efforts (⇒ inherit engine defaults).
    /// Returns true and persists only when the id set actually changed (avoids needless writes).</summary>
    public bool UpdateFromQuery(string engineId, IReadOnlyList<string> queriedModelIds)
    {
        if (queriedModelIds.Count == 0) return false; // a failed/empty query must not wipe the catalog
        _engines.TryGetValue(engineId, out var cat);
        var existing = cat?.Models ?? [];
        var byId = existing.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);

        var oldIds = existing.Select(m => m.Id);
        if (existing.Count == queriedModelIds.Count && oldIds.SequenceEqual(queriedModelIds, StringComparer.OrdinalIgnoreCase))
            return false; // unchanged

        var merged = queriedModelIds
            .Select(id => byId.TryGetValue(id, out var prev) ? prev : new ModelEntry(id))
            .ToArray();
        _engines[engineId] = new EngineCatalog(cat?.DefaultEfforts ?? [], merged);
        try { Save(); } catch { /* best-effort */ }
        return true;
    }

    /// <summary>Serialize the catalog to <see cref="_path"/> (pretty, atomic via temp+replace).</summary>
    public void Save()
    {
        var dto = new CatalogDto
        {
            SchemaVersion = 1,
            Engines = _engines.ToDictionary(
                kv => kv.Key,
                kv => new EngineDto
                {
                    DefaultEfforts = [.. kv.Value.DefaultEfforts],
                    Models = [.. kv.Value.Models.Select(m => new ModelDto { Id = m.Id, Efforts = m.Efforts?.ToArray(), DefaultEffort = m.DefaultEffort })],
                }),
        };
        var json = JsonSerializer.Serialize(dto, JsonOpts);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private static ModelEntry? Find(EngineCatalog cat, string? modelId) =>
        string.IsNullOrWhiteSpace(modelId) ? null
            : cat.Models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));

    // ----- parsing (lenient: a model entry may be a bare string OR an object) -----

    private static Dictionary<string, EngineCatalog> Parse(string json)
    {
        var result = new Dictionary<string, EngineCatalog>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("engines", out var engines) || engines.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var eng in engines.EnumerateObject())
        {
            var defaults = StrArray(eng.Value, "defaultEfforts");
            var models = new List<ModelEntry>();
            if (eng.Value.TryGetProperty("models", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.String)
                    {
                        var id = m.GetString();
                        if (!string.IsNullOrWhiteSpace(id)) models.Add(new ModelEntry(id!));
                    }
                    else if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var idEl)
                             && idEl.GetString() is { Length: > 0 } id)
                    {
                        var efforts = m.TryGetProperty("efforts", out var ef) && ef.ValueKind == JsonValueKind.Array ? StrArrayOf(ef) : null;
                        var def = m.TryGetProperty("defaultEffort", out var de) && de.ValueKind == JsonValueKind.String ? de.GetString() : null;
                        models.Add(new ModelEntry(id, efforts, def));
                    }
                }
            result[eng.Name] = new EngineCatalog(defaults, models);
        }
        return result;
    }

    private static string[] StrArray(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array ? StrArrayOf(el) : [];
    private static string[] StrArrayOf(JsonElement arr) =>
        [.. arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // engines/defaultEfforts/models/id/efforts — matches Parse()
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep model labels readable
    };

    private sealed class CatalogDto
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, EngineDto> Engines { get; set; } = new();
    }
    private sealed class EngineDto
    {
        public string[] DefaultEfforts { get; set; } = [];
        public ModelDto[] Models { get; set; } = [];
    }
    private sealed class ModelDto
    {
        public string Id { get; set; } = "";
        public string[]? Efforts { get; set; }
        public string? DefaultEffort { get; set; }
    }
}
