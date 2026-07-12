namespace AgentManager.Core.Engines;

/// <summary>One model within an engine's config: its id plus optional per-model reasoning options/default and
/// whether it is a "preferred" model (surfaced in the composer's quick picker). Absorbs what used to live split
/// across <c>models.json</c> (efforts) and settings.json <c>PreferredModels</c>.</summary>
public sealed record EngineModelConfig(
    string Id,
    IReadOnlyList<string>? Efforts = null,
    string? DefaultEffort = null,
    bool Preferred = false);

/// <summary>Per-engine authentication config. API key is stored DPAPI-encrypted (base64) — never plaintext.</summary>
public sealed record EngineAuthConfig(
    string Mode = "subscription",   // subscription (CLI login) | api
    string ApiKeyEnc = "",
    bool AutoApiOnLimit = false);

/// <summary>How to launch a CUSTOM engine's process (built-in engines resolve via their adapter instead).
/// Args are passed as an argument LIST (never a shell string); <c>{prompt}</c> etc. are substituted by the adapter.</summary>
public sealed record EngineLaunchConfig(
    string Exe = "",
    IReadOnlyList<string>? Args = null);

/// <summary>
/// The complete configuration for ONE engine — built-in (cc/gx/agy/pi/pi-worker) or user-defined (custom).
/// Persisted as <c>%LOCALAPPDATA%/AgentManager/engines/&lt;id&gt;.json</c>, one file per engine. This is the single
/// home for everything engine-specific that used to be scattered across settings.json (per-engine path fields,
/// <c>DefaultModels</c>/<c>PreferredModels</c>/<c>SkillDirs</c>/<c>EngineAuthMode</c>/<c>EngineApiKey</c>/
/// <c>EngineAutoApiOnLimit</c>/<c>DisabledEngines</c>) and <c>models.json</c>. For a custom engine the same file
/// ALSO carries its identity + <see cref="AdapterKind"/> + <see cref="Launch"/> (it doubles as the manifest).
/// Common/global settings stay in settings.json; transient runtime state (usage, rate-limit cooldowns) lives in state.
/// </summary>
public sealed record EngineConfig(
    string Id,
    string Name,
    string Badge = "",
    string Source = "builtin",       // builtin | custom
    string AdapterKind = "",         // claude-stream-json | codex-json | codex-app-server | agy-pty | pi-rpc | one-shot-text | agentmanager-bridge-jsonl (alias: bridge-jsonl) | acp
    string Cli = "",
    string Desc = "",
    string InstallUrl = "",
    bool Enabled = true,
    string Path = "",                // user override for the CLI/entry path; empty = auto-detect
    EngineLaunchConfig? Launch = null,
    EngineAuthConfig? Auth = null,
    string SkillDir = "",
    string? DefaultModel = null,
    IReadOnlyList<string>? DefaultEfforts = null,   // engine-level fallback effort list (models omitting their own)
    IReadOnlyList<EngineModelConfig>? Models = null,
    IReadOnlyList<string>? AllowedRoles = null,       // Plain | Main | Worker
    IReadOnlyList<string>? ModelsQuery = null)        // custom engines: args that print the model list, one id per line (e.g. opencode ["models"]) — powers the "모델 조회" button
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<EngineModelConfig> ModelList => Models ?? [];
    [System.Text.Json.Serialization.JsonIgnore]
    public EngineAuthConfig AuthOrDefault => Auth ?? new EngineAuthConfig();

    /// <summary>Model ids only, in order.</summary>
    public IReadOnlyList<string> ModelIds() => ModelList.Select(m => m.Id).ToArray();

    /// <summary>Preferred (composer quick-pick) model ids.</summary>
    public IReadOnlyList<string> PreferredModelIds() => ModelList.Where(m => m.Preferred).Select(m => m.Id).ToArray();

    /// <summary>Effort options for a model: its own list if present, else the engine's <see cref="DefaultEfforts"/>.</summary>
    public IReadOnlyList<string> EffortsFor(string? modelId)
    {
        var m = Find(modelId);
        return m?.Efforts ?? DefaultEfforts ?? [];
    }

    /// <summary>The model's default effort (picker "(default)"), or null.</summary>
    public string? DefaultEffortFor(string? modelId) => Find(modelId)?.DefaultEffort;

    /// <summary>Whether the engine has any reasoning dimension at all (false ⇒ hide the effort picker).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasEfforts => (DefaultEfforts?.Count ?? 0) > 0;

    private EngineModelConfig? Find(string? modelId) =>
        string.IsNullOrWhiteSpace(modelId) ? null
            : ModelList.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
}
