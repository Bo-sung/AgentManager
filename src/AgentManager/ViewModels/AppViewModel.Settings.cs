using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

/// <summary>UI 언어 선택 항목 (id = ko|en, Name = 표시명).</summary>
public sealed record LanguageDef(string Id, string Name);

public sealed partial class AppViewModel
{
    // ----- settings overlay state -----
    private bool _showSettings;
    public bool ShowSettings { get => _showSettings; set => Set(ref _showSettings, value); }
    private string _settingsClaudePath = "";
    public string SettingsClaudePath { get => _settingsClaudePath; set => Set(ref _settingsClaudePath, value); }
    private string _settingsCodexPath = "";
    public string SettingsCodexPath { get => _settingsCodexPath; set => Set(ref _settingsCodexPath, value); }
    private string _settingsOllamaEndpoint = "";
    public string SettingsOllamaEndpoint { get => _settingsOllamaEndpoint; set => Set(ref _settingsOllamaEndpoint, value); }
    private string _settingsOllamaModel = "";
    public string SettingsOllamaModel { get => _settingsOllamaModel; set => Set(ref _settingsOllamaModel, value); }
    private bool _settingsDefaultTranslationEnabled = true;
    public bool SettingsDefaultTranslationEnabled { get => _settingsDefaultTranslationEnabled; set => Set(ref _settingsDefaultTranslationEnabled, value); }
    private bool _settingsWarnNoWorktree;
    public bool SettingsWarnNoWorktree { get => _settingsWarnNoWorktree; set => Set(ref _settingsWarnNoWorktree, value); }
    private string _settingsTheme = "dark";
    public string SettingsTheme
    {
        get => _settingsTheme;
        // 테마는 라이브 적용(accent와 동일). 팔레트 교체 후 사용자의 강조색을 재적용한다.
        set
        {
            if (Set(ref _settingsTheme, value))
            {
                Theme.ThemePalette.Apply(value);
                Theme.AccentPalette.Apply(Theme.AccentPalette.Normalize(SettingsAccent));
            }
        }
    }
    /// <summary>설정 UI의 테마 선택지 (다크/라이트 + IDE 프리셋).</summary>
    public IReadOnlyList<Theme.ThemeDef> AvailableThemes => Theme.ThemePalette.All;
    private string _theme = "dark";
    private string _settingsLanguage = "ko";
    /// <summary>설정 UI 언어 선택(ko|en) — 드롭다운 바인딩.</summary>
    public string SettingsLanguage { get => _settingsLanguage; set => Set(ref _settingsLanguage, value); }
    /// <summary>언어 선택지 (id + 표시명).</summary>
    public IReadOnlyList<LanguageDef> AvailableLanguages { get; } = [new("ko", "한국어"), new("en", "English")];
    private string _language = "ko";

    /// <summary>비-git 폴더에서 "격리 없이 실행" 안내를 띄울지 (기본 끔 — 비-git 사용이 일반 흐름인 사용자 배려).</summary>
    private bool _warnNoWorktree;

    /// <summary>새 세션 기본 승인 정책: ask | safe | yolo. RequireApproval + Sandbox 둘 다 시드.</summary>
    private string _approvalPolicy = "yolo";
    private string _settingsApprovalPolicy = "yolo";
    public string SettingsApprovalPolicy { get => _settingsApprovalPolicy; set => Set(ref _settingsApprovalPolicy, value); }

    // ----- orchestration -----
    private string _worktreeBase = "";
    public string SettingsWorktreeBase { get => _settingsWorktreeBase; set => Set(ref _settingsWorktreeBase, value); }
    private string _settingsWorktreeBase = "";
    private bool _autoStartLastSession;
    public bool SettingsAutoStart { get => _settingsAutoStart; set => Set(ref _settingsAutoStart, value); }
    private bool _settingsAutoStart;
    private bool _streamLogs = true;
    /// <summary>목록의 실시간 활동 표시 여부 (즉시 반영, 영속).</summary>
    public bool StreamLogs { get => _streamLogs; set => Set(ref _streamLogs, value); }
    public bool SettingsStreamLogs { get => _settingsStreamLogs; set => Set(ref _settingsStreamLogs, value); }
    private bool _settingsStreamLogs = true;

    // ----- per-engine default model -----
    private Dictionary<string, string> _defaultModels = new();
    public string[] CcModels => EngineModels("cc");
    public string[] GxModels => EngineModels("gx");
    public string[] AgyModels => EngineModels("agy");
    private string[] EngineModels(string id) => Array.Find(AllEngines, e => e.Id == id)?.Models ?? [];
    /// <summary>엔진의 기본 모델 (설정값 → 없으면 첫 모델).</summary>
    private string DefaultModelFor(string id) =>
        _defaultModels.TryGetValue(id, out var m) && !string.IsNullOrWhiteSpace(m) ? m : (EngineModels(id).FirstOrDefault() ?? "");
    /// <summary>유효한 모델만 저장 (엔진 모델 목록에 있을 때).</summary>
    private void SetDefaultModel(string id, string model)
    {
        if (!string.IsNullOrWhiteSpace(model) && Array.IndexOf(EngineModels(id), model) >= 0)
            _defaultModels[id] = model;
        else
            _defaultModels.Remove(id);
    }
    public string SettingsModelCc { get => _settingsModelCc; set => Set(ref _settingsModelCc, value); }
    private string _settingsModelCc = "";
    public string SettingsModelGx { get => _settingsModelGx; set => Set(ref _settingsModelGx, value); }
    private string _settingsModelGx = "";
    public string SettingsModelAgy { get => _settingsModelAgy; set => Set(ref _settingsModelAgy, value); }
    private string _settingsModelAgy = "";

    // ----- per-engine enable/disable -----
    public bool SettingsEngineCc { get => _settingsEngineCc; set => Set(ref _settingsEngineCc, value); }
    private bool _settingsEngineCc = true;
    public bool SettingsEngineGx { get => _settingsEngineGx; set => Set(ref _settingsEngineGx, value); }
    private bool _settingsEngineGx = true;
    public bool SettingsEngineAgy { get => _settingsEngineAgy; set => Set(ref _settingsEngineAgy, value); }
    private bool _settingsEngineAgy = true;

    // ----- per-engine auth (subscription / api key) -----
    private readonly Dictionary<string, string> _engineAuthMode = new();   // id → "subscription" | "api"
    private readonly Dictionary<string, string> _engineApiKey = new();     // id → DPAPI base64
    /// <summary>엔진의 API 키 env 변수명 (cc/gx). 없으면 null.</summary>
    private static string? ApiEnvVar(string id) => id switch
    {
        "cc" => "ANTHROPIC_API_KEY",
        "gx" => "OPENAI_API_KEY",
        _ => null,
    };
    /// <summary>실행 시 주입할 env: api 모드 + 키 있음 → { 변수명: 복호화키 }.</summary>
    private IReadOnlyDictionary<string, string> ApiEnvFor(string id)
    {
        if (_engineAuthMode.GetValueOrDefault(id, "subscription") != "api") return EmptyEnv;
        if (ApiEnvVar(id) is not { } var) return EmptyEnv;
        var key = Persistence.Dpapi.Decrypt(_engineApiKey.GetValueOrDefault(id, ""));
        return string.IsNullOrWhiteSpace(key) ? EmptyEnv : new Dictionary<string, string> { [var] = key };
    }
    private static readonly Dictionary<string, string> EmptyEnv = new();
    private void SaveEngineAuth(string id, string mode, string plainKey)
    {
        _engineAuthMode[id] = mode == "api" ? "api" : "subscription";
        if (!string.IsNullOrWhiteSpace(plainKey))
            _engineApiKey[id] = Persistence.Dpapi.Encrypt(plainKey.Trim());
        else
            _engineApiKey.Remove(id);
    }
    public string SettingsAuthCc { get => _settingsAuthCc; set => Set(ref _settingsAuthCc, value); }
    private string _settingsAuthCc = "subscription";
    public string SettingsApiKeyCc { get => _settingsApiKeyCc; set => Set(ref _settingsApiKeyCc, value); }
    private string _settingsApiKeyCc = "";
    public string SettingsAuthGx { get => _settingsAuthGx; set => Set(ref _settingsAuthGx, value); }
    private string _settingsAuthGx = "subscription";
    public string SettingsApiKeyGx { get => _settingsApiKeyGx; set => Set(ref _settingsApiKeyGx, value); }
    private string _settingsApiKeyGx = "";

    // ----- appearance: accent / density / telemetry -----
    private string _accent = "ember";
    /// <summary>선택된 강조색 (즉시 라이브 적용). Cancel 시 저장값으로 되돌림.</summary>
    public string SettingsAccent
    {
        get => _settingsAccent;
        set { if (Set(ref _settingsAccent, value)) Theme.AccentPalette.Apply(value); }
    }
    private string _settingsAccent = "ember";
    private string _density = "comfortable";
    public string SettingsDensity { get => _settingsDensity; set => Set(ref _settingsDensity, value); }
    private string _settingsDensity = "comfortable";
    /// <summary>밀도 → 루트 콘텐츠 스케일 (라이브).</summary>
    public double DensityScale => _density == "compact" ? 0.92 : 1.0;
    private bool _telemetry;
    public bool SettingsTelemetry { get => _settingsTelemetry; set => Set(ref _settingsTelemetry, value); }
    private bool _settingsTelemetry;

    private static (bool requireApproval, SandboxMode sandbox) PolicyToSession(string policy) => policy switch
    {
        "ask" => (true, SandboxMode.ReadOnly),
        "safe" => (false, SandboxMode.WorkspaceWrite),
        _ => (false, SandboxMode.DangerFullAccess), // yolo
    };

    // provider detection status (settings panel)
    /// <summary>엔진의 CLI를 프로젝트 경로의 새 터미널로 열어 사용자가 직접 로그인하게 한다
    /// (명령 추측 없이 각 CLI 자체 인증 플로우 사용).</summary>
    public void SignIn(string engineId)
    {
        var exe = EngineRegistry.ResolveExe(engineId, _claudePath, _codexPath);
        if (string.IsNullOrWhiteSpace(exe))
        {
            SettingsStatus = L("L.SignInNotFound");
            return;
        }
        var cwd = WorkingDirectory;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"\"{exe}\"\"",       // 콘솔을 열고 CLI 실행 후 창 유지
                WorkingDirectory = Directory.Exists(cwd) ? cwd : Environment.CurrentDirectory,
                UseShellExecute = true,
            });
            SettingsStatus = L("L.SignInLaunched");
        }
        catch (Exception ex)
        {
            SettingsStatus = ex.Message;
        }
    }

    public string ClaudeDetectLabel => DetectLabel("cc", _claudePath);
    public string CodexDetectLabel => DetectLabel("gx", _codexPath);
    public string AgyDetectLabel => DetectLabel("agy", null);
    private static string DetectLabel(string id, string? overridePath)
    {
        var exe = EngineRegistry.ResolveExe(id, id == "cc" ? overridePath : null, id == "gx" ? overridePath : null);
        if (exe is null) return AgentManager.App.L("L.DetectMissing");
        if (File.Exists(exe)) return AgentManager.App.L("L.DetectPathPrefix", exe);
        return AgentManager.App.L("L.DetectPathDependent", exe); // bare command name — resolved at spawn time
    }
    private void RefreshDetectLabels()
    {
        OnChanged(nameof(ClaudeDetectLabel)); OnChanged(nameof(CodexDetectLabel));
        OnChanged(nameof(AgyDetectLabel));
        OnChanged(nameof(CcAccount)); OnChanged(nameof(GxAccount));
        OnChanged(nameof(AgyAccount));
    }

    // 각 CLI의 로그인 계정(구독 인증) — 이메일, 미로그인 시 "". UI: 비어있으면 Sign in, 있으면 계정 표시.
    public string CcAccount => Persistence.EngineAccounts.For("cc") ?? "";
    public string GxAccount => Persistence.EngineAccounts.For("gx") ?? "";
    public string AgyAccount => Persistence.EngineAccounts.For("agy") ?? "";
    private string _settingsStatus = "";
    public string SettingsStatus { get => _settingsStatus; set => Set(ref _settingsStatus, value); }

    /// <summary>VM 필드 → 설정 에디터(Settings* 미러) 프로퍼티로 끌어온다. OpenSettings + 외부 리로드에서 공용.</summary>
    private void PullSettingsToEditor()
    {
        SettingsClaudePath = _claudePath;
        SettingsCodexPath = _codexPath;
        SettingsOllamaEndpoint = _ollamaEndpoint;
        SettingsOllamaModel = _ollamaModel;
        SettingsDefaultTranslationEnabled = TranslationEnabled;
        SettingsWarnNoWorktree = _warnNoWorktree;
        SettingsTheme = Theme.ThemePalette.Normalize(_theme);
        SettingsLanguage = _language == "en" ? "en" : "ko";
        SettingsApprovalPolicy = _approvalPolicy;
        SettingsWorktreeBase = _worktreeBase;
        SettingsAutoStart = _autoStartLastSession;
        SettingsStreamLogs = _streamLogs;
        SettingsModelCc = DefaultModelFor("cc");
        SettingsModelGx = DefaultModelFor("gx");
        SettingsModelAgy = DefaultModelFor("agy");
        SettingsAccent = _accent;
        SettingsDensity = _density;
        SettingsTelemetry = _telemetry;
        SettingsEngineCc = !_disabledEngines.Contains("cc");
        SettingsEngineGx = !_disabledEngines.Contains("gx");
        SettingsEngineAgy = !_disabledEngines.Contains("agy");
        SettingsAuthCc = _engineAuthMode.GetValueOrDefault("cc", "subscription");
        SettingsAuthGx = _engineAuthMode.GetValueOrDefault("gx", "subscription");
        SettingsApiKeyCc = Persistence.Dpapi.Decrypt(_engineApiKey.GetValueOrDefault("cc", ""));
        SettingsApiKeyGx = Persistence.Dpapi.Decrypt(_engineApiKey.GetValueOrDefault("gx", ""));
        RefreshDetectLabels(); // CLI 경로 감지 라벨 + 로그인 계정 표시 갱신
    }

    private void OpenSettings()
    {
        PullSettingsToEditor();
        SettingsStatus = "";
        if (CurrentView != MainViewKind.Settings) _viewBeforeSettings = CurrentView;
        CurrentView = MainViewKind.Settings;
    }

    /// <summary>settings.json을 기본 편집기로 연다(없으면 먼저 기록). VS Code식 손편집 진입점.</summary>
    private void OpenSettingsFile()
    {
        try
        {
            if (!System.IO.File.Exists(Persistence.SettingsStore.SettingsPath))
                Persistence.SettingsStore.Save(BuildSettingsDto());
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Persistence.SettingsStore.SettingsPath,
                UseShellExecute = true,
            });
        }
        catch { }
    }
    private MainViewKind _viewBeforeSettings = MainViewKind.Orchestrator;
    private void CloseSettings()
    {
        // 라이브 미리보기한 테마/강조색을 저장값으로 되돌린다
        Theme.ThemePalette.Apply(_theme);
        Theme.AccentPalette.Apply(_accent);
        ShowSettings = false;
        CurrentView = _viewBeforeSettings;
    }

    private void SaveSettings()
    {
        _claudePath = Clean(SettingsClaudePath);
        _codexPath = Clean(SettingsCodexPath);
        RefreshDetectLabels();
        _ollamaEndpoint = string.IsNullOrWhiteSpace(SettingsOllamaEndpoint) ? "http://localhost:11434" : SettingsOllamaEndpoint.Trim();
        _ollamaModel = string.IsNullOrWhiteSpace(SettingsOllamaModel) ? "exaone3.5:7.8b" : SettingsOllamaModel.Trim();
        TranslationEnabled = SettingsDefaultTranslationEnabled;
        _warnNoWorktree = SettingsWarnNoWorktree;
        _approvalPolicy = SettingsApprovalPolicy is "ask" or "safe" ? SettingsApprovalPolicy : "yolo";
        _worktreeBase = (SettingsWorktreeBase ?? "").Trim();
        _autoStartLastSession = SettingsAutoStart;
        StreamLogs = SettingsStreamLogs;
        SetDefaultModel("cc", SettingsModelCc);
        SetDefaultModel("gx", SettingsModelGx);
        SetDefaultModel("agy", SettingsModelAgy);
        _accent = Theme.AccentPalette.Normalize(SettingsAccent);
        _density = SettingsDensity == "compact" ? "compact" : "comfortable";
        OnChanged(nameof(DensityScale));
        _telemetry = SettingsTelemetry;
        // 엔진 비활성 집합 재구성 — 단, 최소 1개는 활성으로 유지
        var disabled = new HashSet<string>();
        if (!SettingsEngineCc) disabled.Add("cc");
        if (!SettingsEngineGx) disabled.Add("gx");
        if (!SettingsEngineAgy) disabled.Add("agy");
        if (disabled.Count < AllEngines.Length)
        {
            _disabledEngines.Clear();
            foreach (var d in disabled) _disabledEngines.Add(d);
            OnChanged(nameof(Engines));
        }
        SaveEngineAuth("cc", SettingsAuthCc, SettingsApiKeyCc);
        SaveEngineAuth("gx", SettingsAuthGx, SettingsApiKeyGx);
        _theme = Theme.ThemePalette.Normalize(SettingsTheme); // 테마는 이미 라이브 적용됨
        var newLanguage = SettingsLanguage == "en" ? "en" : "ko";
        var languageChanged = newLanguage != _language;
        _language = newLanguage;
        _translator = CreateTranslator(_ollamaEndpoint, _ollamaModel);
        SettingsStatus = languageChanged ? L("L.SettingsSavedRestart") : L("L.SettingsSaved");
        SaveState();
    }

    private static string Clean(string value) => value.Trim().Trim('"');

}
