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
    /// <summary>Override path to the pi-worker launcher (harness <c>dist/cli/index.js</c> or a real
    /// executable). Empty = auto-detect (npm-global @agentmanager/pi-worker-harness). Only used for
    /// Worker-role pi sessions; General/Main pi keeps using <see cref="PiPath"/>.</summary>
    public string PiWorkerPath { get; set; } = "";

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

    /// <summary>SEC (egress guard): allow the built-in Ollama translator to target a NON-loopback endpoint.
    /// Default false = restrict to loopback so the user's raw prompt text can't silently egress to an
    /// arbitrary external host (a corrupted/hand-edited settings.json can't bypass it). Opt in only for a
    /// trusted Ollama server on your LAN. See <see cref="OllamaTranslator.IsLoopbackEndpoint"/>.</summary>
    public bool AllowRemoteOllamaEndpoint { get; set; } = false;

    /// <summary>Ollama 번역 요청 타임아웃(초). 큰 모델(예: 12B)은 콜드로드/긴 응답에서 기본 60초를 넘겨
    /// <see cref="OllamaTranslator"/>가 null을 반환 → 번역이 조용히 원문으로 폴백되므로 설정에서 선택 가능하게 노출.
    /// 첫 시도 후 재시도는 이 값의 2배까지 늘린다. 유효 범위 10~600초.</summary>
    public int OllamaTimeoutSeconds { get; set; } = 60;

    /// <summary>Model the agent provider uses for translation (blank = engine default). Applies to the selected
    /// <c>agent:&lt;id&gt;</c> — e.g. a cheap/fast model like cc's haiku so translation doesn't burn the default.</summary>
    public string TranslationAgentModel { get; set; } = "";

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
