using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentManager.Core.Engines;

/// <summary>
/// Per-engine config store: one JSON file per engine under <c>%LOCALAPPDATA%/AgentManager/engines/</c>. Built-in
/// engines are seeded from <see cref="DefaultEngineConfig"/> on first run (file becomes the user's editable
/// override); any additional <c>*.json</c> is loaded as a custom engine. This replaces the per-engine keys that
/// used to live in settings.json (paths, auth, default/preferred models, skill dirs) plus <c>models.json</c>.
/// Robust: a missing or unparseable file for a built-in is re-seeded; a bad custom file is skipped (never throws).
/// </summary>
public sealed class EngineConfigStore
{
    private readonly Dictionary<string, EngineConfig> _byId;
    private readonly List<string> _order; // stable display order: built-ins (seed order) then customs
    private readonly string _dir;

    private EngineConfigStore(string dir, Dictionary<string, EngineConfig> byId, List<string> order)
    {
        _dir = dir;
        _byId = byId;
        _order = order;
    }

    /// <summary>Default engines directory under LocalApplicationData (next to settings.json / state.json).</summary>
    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "engines");

    /// <summary>Load all engine configs, seeding+writing any built-in whose file is missing/corrupt. Never throws.</summary>
    public static EngineConfigStore Load(IReadOnlyList<EngineConfig> defaults, string? dir = null)
    {
        dir ??= DefaultDir;
        var byId = new Dictionary<string, EngineConfig>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        try { Directory.CreateDirectory(dir); } catch { /* seeding is best-effort */ }

        // 1) Load whatever is on disk (built-in overrides + custom engines).
        try
        {
            foreach (var file in Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.json") : [])
            {
                var cfg = TryParseFile(file);
                if (cfg is { } c && !string.IsNullOrWhiteSpace(c.Id) && !byId.ContainsKey(c.Id))
                {
                    byId[c.Id] = c;
                    order.Add(c.Id);
                }
            }
        }
        catch { /* unreadable dir → fall through to defaults */ }

        var store = new EngineConfigStore(dir, byId, order);

        // 2) Seed any built-in default that isn't on disk (in a stable order at the front).
        var seedOrder = new List<string>();
        foreach (var def in defaults)
        {
            seedOrder.Add(def.Id);
            if (!byId.ContainsKey(def.Id))
            {
                byId[def.Id] = def;
                try { store.Save(def); } catch { /* best-effort seed write */ }
            }
        }
        // Rebuild order: built-ins first (seed order), then any custom files not in defaults.
        var final = new List<string>(seedOrder);
        foreach (var id in order) if (!final.Contains(id, StringComparer.OrdinalIgnoreCase)) final.Add(id);
        store._order.Clear();
        store._order.AddRange(final);
        return store;
    }

    /// <summary>All engine configs in stable display order.</summary>
    public IReadOnlyList<EngineConfig> All =>
        _order.Where(_byId.ContainsKey).Select(id => _byId[id]).ToArray();

    public EngineConfig? Get(string id) => _byId.TryGetValue(id, out var c) ? c : null;

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>Add or replace an engine config and persist its file. New ids append to the display order.</summary>
    public void Upsert(EngineConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Id)) return;
        var isNew = !_byId.ContainsKey(cfg.Id);
        _byId[cfg.Id] = cfg;
        if (isNew) _order.Add(cfg.Id);
        try { Save(cfg); } catch { /* best-effort */ }
    }

    /// <summary>Replace an engine's model list from a live query (pi <c>--list-models</c> / agy <c>agy models</c>).
    /// Existing per-model efforts/default/preferred are PRESERVED for surviving ids; new ids get defaults. Empty
    /// query is a no-op (never wipes). Persists + returns true only when the id set actually changed.</summary>
    public bool UpdateModelsFromQuery(string engineId, IReadOnlyList<string> queriedModelIds)
    {
        if (queriedModelIds.Count == 0) return false;
        if (!_byId.TryGetValue(engineId, out var cfg)) return false;
        var byId = cfg.ModelList.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var oldIds = cfg.ModelList.Select(m => m.Id);
        if (cfg.ModelList.Count == queriedModelIds.Count && oldIds.SequenceEqual(queriedModelIds, StringComparer.OrdinalIgnoreCase))
            return false; // unchanged
        var merged = queriedModelIds.Select(id => byId.TryGetValue(id, out var prev) ? prev : new EngineModelConfig(id)).ToArray();
        var updated = cfg with { Models = merged };
        _byId[engineId] = updated;
        try { Save(updated); } catch { /* best-effort */ }
        return true;
    }

    /// <summary>Delete a custom engine (built-ins are not removed — they'd just re-seed). Removes the file.</summary>
    public bool Remove(string id)
    {
        if (!_byId.TryGetValue(id, out var cfg) || cfg.Source != "custom") return false;
        _byId.Remove(id);
        _order.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        try { var p = PathFor(id); if (File.Exists(p)) File.Delete(p); } catch { }
        return true;
    }

    /// <summary>Serialize one engine to <c>engines/&lt;id&gt;.json</c> (pretty, atomic temp+replace).</summary>
    public void Save(EngineConfig cfg)
    {
        Directory.CreateDirectory(_dir);
        var json = JsonSerializer.Serialize(cfg, JsonOpts);
        var path = PathFor(cfg.Id);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private string PathFor(string id) => Path.Combine(_dir, SafeFileName(id) + ".json");

    private static string SafeFileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var name = new string(chars).Trim();
        return string.IsNullOrEmpty(name) ? "engine" : name;
    }

    private static EngineConfig? TryParseFile(string path)
    {
        try { return JsonSerializer.Deserialize<EngineConfig>(File.ReadAllText(path), JsonOpts); }
        catch { return null; } // malformed file → skip (built-ins re-seed; a bad custom is ignored)
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
