using AgentManager.Core.Translation;

namespace AgentManager.Core.Settings;

/// <summary>Headless machine-local app configuration (settings.json) owned by Core, so a CLI and the GUI
/// share ONE source of truth. Per-engine auth/rate-limit lives in <see cref="EngineAuthService"/>; this
/// holds the general config. The GUI ViewModel's backing fields become thin delegating properties over
/// this, so read/write sites are unchanged and there is no duplicate state. Grown incrementally by the
/// headless-core overhaul (step 2b).</summary>
public sealed class SettingsService
{
    // ----- engine executable path overrides (empty = auto-detect) -----
    public string ClaudePath { get; set; } = "";
    public string CodexPath { get; set; } = "";
    public string AgyPath { get; set; } = "";
    public string PiPath { get; set; } = "";

    // ----- translation: language pair + selected provider -----
    public bool TranslationEnabled { get; set; } = true;
    public string TranslateSourceLanguage { get; set; } = "Korean";
    public string TranslateTargetLanguage { get; set; } = "English";

    /// <summary>Which translation provider is active. Built-in ids: <c>"ollama"</c> (uses <see cref="OllamaEndpoint"/>/
    /// <see cref="OllamaModel"/>) and <c>"agent:cc"|"agent:gx"|"agent:pi"</c> (reuse an installed engine); a custom
    /// OpenAI-compatible entry uses its own <see cref="TranslationCustomProvider.Id"/>. Defaults to Ollama so existing
    /// users are unchanged.</summary>
    public string TranslationSelectedId { get; set; } = "ollama";

    // Ollama built-in config (kept for back-compat + the "ollama" provider).
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "exaone3.5:7.8b";

    /// <summary>User-added OpenAI-compatible / cloud endpoints (the "+ Add custom" list). API keys are stored
    /// DPAPI-encrypted by the frontend (<see cref="TranslationCustomProvider.ApiKeyEnc"/> is opaque to Core).</summary>
    public List<TranslationCustomProvider> TranslationCustomProviders { get; set; } = new();

    // ----- orchestration / behavior flags -----
    public bool WarnNoWorktree { get; set; }
    public string ApprovalPolicy { get; set; } = "yolo";
    public string WorktreeBase { get; set; } = "";
    public bool AutoStartLastSession { get; set; }
    public bool StreamLogs { get; set; } = true;
    public bool Telemetry { get; set; }
    public bool ReviewPaneOpen { get; set; } = true;
    public int MaxConcurrentSessions { get; set; } = 3;
    public int MaxConcurrentWorkers { get; set; } = AgentManager.Core.Workers.WorkerDefaults.DefaultMaxConcurrentWorkers;
    public string WorkerBehaviorPreamble { get; set; } = AgentManager.Core.Workers.WorkerDefaults.BehaviorPreamble;
    public string SkillContent { get; set; } = SkillInjector.WorkerPromptDefault;

    // ----- collections (default models · preferred sets · disabled/dismissed · skill dirs) -----
    public Dictionary<string, string> DefaultModels { get; set; } = new();
    public Dictionary<string, string> SkillDirs { get; set; } = SkillInjector.DefaultDirs();
    public HashSet<string> DisabledEngines { get; } = [];
    public HashSet<string> DismissedCliSessions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, HashSet<string>> Preferred { get; } = new()
    {
        ["cc"] = new(StringComparer.OrdinalIgnoreCase),
        ["gx"] = new(StringComparer.OrdinalIgnoreCase),
        ["agy"] = new(StringComparer.OrdinalIgnoreCase),
        ["pi"] = new(StringComparer.OrdinalIgnoreCase),
    };
}
