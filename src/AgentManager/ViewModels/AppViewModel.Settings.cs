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
    private string _settingsAgyPath = "";
    public string SettingsAgyPath { get => _settingsAgyPath; set => Set(ref _settingsAgyPath, value); }
    private string _settingsOllamaEndpoint = "";
    public string SettingsOllamaEndpoint { get => _settingsOllamaEndpoint; set => Set(ref _settingsOllamaEndpoint, value); }
    private string _settingsOllamaModel = "";
    public string SettingsOllamaModel { get => _settingsOllamaModel; set => Set(ref _settingsOllamaModel, value); }

    /// <summary>"설치 모델 조회"로 채워지는 Ollama 모델 목록(드롭다운).</summary>
    public ObservableCollection<string> OllamaModels { get; } = [];
    private string _ollamaModelsStatus = "";
    /// <summary>조회 상태 라벨(조회 중/N개/실패).</summary>
    public string OllamaModelsStatus { get => _ollamaModelsStatus; private set => Set(ref _ollamaModelsStatus, value); }

    /// <summary>현재 엔드포인트의 설치된 Ollama 모델을 조회해 드롭다운을 채운다.</summary>
    public async Task QueryOllamaModelsAsync()
    {
        OllamaModelsStatus = App.L("L.Querying");
        try
        {
            var endpoint = string.IsNullOrWhiteSpace(SettingsOllamaEndpoint) ? "http://localhost:11434" : SettingsOllamaEndpoint;
            var models = await Core.Translation.OllamaTranslator.ListModelsAsync(endpoint);
            OllamaModels.Clear();
            foreach (var m in models) OllamaModels.Add(m);
            OllamaModelsStatus = models.Count > 0 ? App.L("L.ModelsFound", models.Count) : App.L("L.NoModels");
        }
        catch
        {
            OllamaModelsStatus = App.L("L.QueryFailed");
        }
    }

    // ----- Ollama 서비스 상태 (번역 게이팅: 번역 ON 가능 = Ollama 활성 && 유저 작동) -----
    private string _ollamaState = "unknown"; // running | stopped | absent | checking | unknown
    public string OllamaState
    {
        get => _ollamaState;
        private set
        {
            if (!Set(ref _ollamaState, value)) return;
            OnChanged(nameof(OllamaRunning)); OnChanged(nameof(OllamaStopped)); OnChanged(nameof(OllamaAbsent));
            OnChanged(nameof(OllamaStatusText)); OnChanged(nameof(CanTranslate));
            if (value is not ("running" or "checking")) ForceTranslationOff();  // Ollama 불가 → 번역 무조건 OFF
        }
    }
    public bool OllamaRunning => _ollamaState == "running";
    public bool OllamaStopped => _ollamaState == "stopped";   // 설치됐지만 꺼짐
    public bool OllamaAbsent => _ollamaState == "absent";     // 미설치
    /// <summary>번역 레이어 사용 가능 여부 = Ollama 실행 중. (번역 ON 토글 활성 조건)</summary>
    public bool CanTranslate => OllamaRunning;
    public string OllamaStatusText => _ollamaState switch
    {
        "running" => App.L("L.OllamaRunning"),
        "stopped" => App.L("L.OllamaStopped"),
        "absent" => App.L("L.OllamaAbsent"),
        "checking" => App.L("L.OllamaChecking"),
        _ => "",
    };

    /// <summary>Ollama 불가 시 번역 무조건 OFF — 모든 세션 + New Agent 기본값. (게이팅은 유지, 켤 수만 없음)</summary>
    private void ForceTranslationOff()
    {
        foreach (var s in _allSessions) if (s.TranslationEnabled) s.TranslationEnabled = false;
        NewAgentTranslation = false;
    }

    /// <summary>Ollama 상태 갱신: 핑 성공=running, 실패면 설치 여부로 stopped/absent 구분.</summary>
    public async Task RefreshOllamaStatusAsync()
    {
        OllamaState = "checking";
        var endpoint = string.IsNullOrWhiteSpace(SettingsOllamaEndpoint) ? _ollamaEndpoint : SettingsOllamaEndpoint;
        var up = await Core.Translation.OllamaTranslator.PingAsync(endpoint, 1500);
        OllamaState = up ? "running" : (EngineRegistry.OllamaExe() is not null ? "stopped" : "absent");
    }

    /// <summary>설정의 '실행' 버튼 — 설치된 ollama를 'serve'로 백그라운드 기동 후 상태 재확인.</summary>
    public void StartOllama()
    {
        var exe = EngineRegistry.OllamaExe();
        if (exe is null) { OllamaState = "absent"; return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "serve") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 6 && !await Core.Translation.OllamaTranslator.PingAsync(_ollamaEndpoint, 1500); i++) await Task.Delay(1000);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await RefreshOllamaStatusAsync());
        });
    }

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

    // ----- 번역 언어 쌍 (번역 전 = 사용자 언어, 번역 후 = 엔진 전달 언어) -----
    private string _translateSource = "Korean";
    private string _translateTarget = "English";
    private string _settingsTranslateSource = "Korean";
    /// <summary>번역 전 언어(사용자 입력·표시). Id = 프롬프트용 영어 표기.</summary>
    public string SettingsTranslateSource { get => _settingsTranslateSource; set => Set(ref _settingsTranslateSource, value); }
    private string _settingsTranslateTarget = "English";
    /// <summary>번역 후 언어(엔진에 전달, 토큰 절감용). Id = 프롬프트용 영어 표기.</summary>
    public string SettingsTranslateTarget { get => _settingsTranslateTarget; set => Set(ref _settingsTranslateTarget, value); }
    /// <summary>번역 언어 선택지 (Id = 영어 표기, Name = 현지 표기).</summary>
    public IReadOnlyList<LanguageDef> AvailableTranslationLanguages { get; } =
    [
        new("Korean", "한국어"), new("English", "English"), new("Japanese", "日本語"),
        new("Chinese", "中文"), new("Spanish", "Español"), new("French", "Français"),
        new("German", "Deutsch"), new("Russian", "Русский"), new("Portuguese", "Português"),
        new("Italian", "Italiano"), new("Vietnamese", "Tiếng Việt"),
    ];

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
    private readonly Dictionary<string, bool> _engineAutoApi = new();      // id → 한도 도달 시 API 자동 전환(opt-in)
    private readonly Dictionary<string, long> _engineLimitedUntil = new(); // id → rate-limit 차단 해제(unix). 실제 실패 시 기록

    public bool HasApiKey(string id) => !string.IsNullOrWhiteSpace(Persistence.Dpapi.Decrypt(_engineApiKey.GetValueOrDefault(id, "")));
    public bool AutoApiOnLimit(string id) => _engineAutoApi.GetValueOrDefault(id);
    /// <summary>한도 소진 상태인가 — 사용량 ~100% 또는 실제 rate-limit 실패(리셋 전).</summary>
    public bool IsEngineLimited(string id)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_engineLimitedUntil.TryGetValue(id, out var until) && until > now) return true;
        if (_usage.TryGetValue(id, out var s) && s.Utilization >= 0.999 && s.ResetsAtUnix > now) return true;
        return false;
    }
    /// <summary>소진 시 API로 자동 전환되어 계속 사용 가능한가(토글 ON + 키 보유).</summary>
    public bool WillUseApiOnLimit(string id) => AutoApiOnLimit(id) && HasApiKey(id);
    /// <summary>rate-limit 실제 실패를 기록 — 리셋 시각(모르면 +1h)까지 소진 처리.</summary>
    public void MarkRateLimited(string id, long resetUnix)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _engineLimitedUntil[id] = resetUnix > now ? resetUnix : now + 3600;
    }

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
        if (ApiEnvVar(id) is not { } var) return EmptyEnv;
        // 명시적 API 모드 OR (한도 소진 + 자동전환 ON + 키 보유) → API 키 주입
        var useApi = _engineAuthMode.GetValueOrDefault(id, "subscription") == "api"
                     || (IsEngineLimited(id) && WillUseApiOnLimit(id));
        if (!useApi) return EmptyEnv;
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
    private bool _settingsAutoApiCc;
    public bool SettingsAutoApiCc { get => _settingsAutoApiCc; set => Set(ref _settingsAutoApiCc, value); }
    private bool _settingsAutoApiGx;
    public bool SettingsAutoApiGx { get => _settingsAutoApiGx; set => Set(ref _settingsAutoApiGx, value); }

    // ----- appearance: accent / density / telemetry -----
    private string _accent = "ember";
    /// <summary>선택된 강조색 (즉시 라이브 적용). Cancel 시 저장값으로 되돌림.</summary>
    public string SettingsAccent
    {
        get => _settingsAccent;
        set { if (Set(ref _settingsAccent, value)) Theme.AccentPalette.Apply(value); }
    }
    private string _settingsAccent = "ember";
    /// <summary>커스텀 강조색 hex 입력. 유효하면 즉시 SettingsAccent로 적용(라이브 미리보기).</summary>
    private string _settingsAccentCustom = "";
    public string SettingsAccentCustom
    {
        get => _settingsAccentCustom;
        set { if (Set(ref _settingsAccentCustom, value) && Theme.AccentPalette.IsHex(value)) SettingsAccent = value.Trim(); }
    }

    // ----- UI 줌: 본문/모달 독립 배율 (Ctrl+휠·단축키는 활성 영역만 조정) -----
    private static double ClampZoom(double v) => Math.Clamp(Math.Round(v, 2), 0.5, 2.0);

    private double _bodyScale = 1.0;
    /// <summary>본문(사이드바+콘텐츠) 스케일. clamp 0.5~2.0. Body Grid가 바인딩.</summary>
    public double BodyScale
    {
        get => _bodyScale;
        set { if (Set(ref _bodyScale, ClampZoom(value))) { OnChanged(nameof(BodyScalePercent)); DebouncedZoomSave(); } }
    }
    private double _modalScale = 1.0;
    /// <summary>모달(오버레이) 스케일. 본문과 독립. clamp 0.5~2.0. 모달들이 바인딩.</summary>
    public double ModalScale
    {
        get => _modalScale;
        set { if (Set(ref _modalScale, ClampZoom(value))) { OnChanged(nameof(ModalScalePercent)); DebouncedZoomSave(); } }
    }
    /// <summary>설정 드롭다운용 퍼센트(양방향).</summary>
    public int BodyScalePercent { get => (int)Math.Round(_bodyScale * 100); set => BodyScale = value / 100.0; }
    public int ModalScalePercent { get => (int)Math.Round(_modalScale * 100); set => ModalScale = value / 100.0; }
    /// <summary>줌 퍼센트 선택지(50~200%, 10% 간격).</summary>
    public int[] ZoomPercentOptions { get; } = [50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200];

    /// <summary>활성 줌 영역 = 모달이 하나라도 열려 있으면 모달, 아니면 본문.</summary>
    public bool IsModalActive => ShowNewAgent || ShowWorkerAssign || ShowNoIdleWorker || ShowAbout || ShowNewProject || ShowNewSchedule || ShowInstallGuide;

    /// <summary>Ctrl+휠·단축키: 활성 영역(모달 열림 시 모달, 아니면 본문)만 조정.</summary>
    public void ZoomBy(int direction)
    {
        if (IsModalActive) { ModalScale += direction * 0.1; FlashZoomToast("모달", _modalScale); }
        else { BodyScale += direction * 0.1; FlashZoomToast("본문", _bodyScale); }
    }
    public void ZoomReset()
    {
        if (IsModalActive) { ModalScale = 1.0; FlashZoomToast("모달", 1.0); }
        else { BodyScale = 1.0; FlashZoomToast("본문", 1.0); }
    }

    // 줌 % 토스트 (변경 시 잠깐 표시)
    private string _zoomToastText = "";
    public string ZoomToastText { get => _zoomToastText; private set => Set(ref _zoomToastText, value); }
    private bool _showZoomToast;
    public bool ShowZoomToast { get => _showZoomToast; private set => Set(ref _showZoomToast, value); }
    private DispatcherTimer? _zoomToastTimer;
    private DispatcherTimer? _zoomSaveTimer;
    private void FlashZoomToast(string region, double scale)
    {
        ZoomToastText = $"{region} {(int)Math.Round(scale * 100)}%";
        ShowZoomToast = true;
        _zoomToastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.1) };
        _zoomToastTimer.Tick -= ZoomToastTick; _zoomToastTimer.Tick += ZoomToastTick;
        _zoomToastTimer.Stop(); _zoomToastTimer.Start();
    }
    private void ZoomToastTick(object? sender, EventArgs e) { _zoomToastTimer!.Stop(); ShowZoomToast = false; }
    private void DebouncedZoomSave()
    {
        _zoomSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _zoomSaveTimer.Tick -= ZoomSaveTick; _zoomSaveTimer.Tick += ZoomSaveTick;
        _zoomSaveTimer.Stop(); _zoomSaveTimer.Start();
    }
    private void ZoomSaveTick(object? sender, EventArgs e) { _zoomSaveTimer!.Stop(); SaveState(); }
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
        var exe = EngineRegistry.ResolveExe(engineId, _claudePath, _codexPath, _agyPath);
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
    public string AgyDetectLabel => DetectLabel("agy", _agyPath);
    private static string DetectLabel(string id, string? overridePath)
    {
        var exe = EngineRegistry.ResolveExe(id,
            id == "cc" ? overridePath : null,
            id == "gx" ? overridePath : null,
            id == "agy" ? overridePath : null);
        if (exe is null) return AgentManager.App.L("L.DetectMissing");
        if (File.Exists(exe)) return AgentManager.App.L("L.DetectPathPrefix", exe);
        return AgentManager.App.L("L.DetectPathDependent", exe); // bare command name — resolved at spawn time
    }

    /// <summary>'탐지' 버튼 — 오토 탐지만 수행해 결과를 해당 경로 입력란에 채운다(수동값 무시). 못 찾으면 입력값 보존.</summary>
    public void DetectEnginePath(string id)
    {
        var found = EngineRegistry.DetectExe(id);
        if (found is null) { SettingsStatus = AgentManager.App.L("L.DetectFailed"); return; }
        switch (id)
        {
            case "cc": SettingsClaudePath = found; break;
            case "gx": SettingsCodexPath = found; break;
            case "agy": SettingsAgyPath = found; break;
        }
        SettingsStatus = AgentManager.App.L("L.DetectFound", found);
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
        SettingsAgyPath = _agyPath;
        SettingsOllamaEndpoint = _ollamaEndpoint;
        SettingsOllamaModel = _ollamaModel;
        SettingsDefaultTranslationEnabled = TranslationEnabled;
        SettingsTranslateSource = _translateSource;
        SettingsTranslateTarget = _translateTarget;
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
        SettingsAccentCustom = Theme.AccentPalette.IsHex(_accent) ? _accent : "";
        SettingsTelemetry = _telemetry;
        SettingsEngineCc = !_disabledEngines.Contains("cc");
        SettingsEngineGx = !_disabledEngines.Contains("gx");
        SettingsEngineAgy = !_disabledEngines.Contains("agy");
        SettingsAuthCc = _engineAuthMode.GetValueOrDefault("cc", "subscription");
        SettingsAuthGx = _engineAuthMode.GetValueOrDefault("gx", "subscription");
        SettingsAutoApiCc = _engineAutoApi.GetValueOrDefault("cc");
        SettingsAutoApiGx = _engineAutoApi.GetValueOrDefault("gx");
        SettingsApiKeyCc = Persistence.Dpapi.Decrypt(_engineApiKey.GetValueOrDefault("cc", ""));
        SettingsApiKeyGx = Persistence.Dpapi.Decrypt(_engineApiKey.GetValueOrDefault("gx", ""));
        RefreshDetectLabels(); // CLI 경로 감지 라벨 + 로그인 계정 표시 갱신
    }

    private void OpenSettings()
    {
        PullSettingsToEditor();
        SettingsStatus = "";
        _ = RefreshOllamaStatusAsync();   // 설정 열 때 Ollama 상태 최신화
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
        _agyPath = Clean(SettingsAgyPath);
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
        _engineAutoApi["cc"] = SettingsAutoApiCc;
        _engineAutoApi["gx"] = SettingsAutoApiGx;
        _theme = Theme.ThemePalette.Normalize(SettingsTheme); // 테마는 이미 라이브 적용됨
        var newLanguage = SettingsLanguage == "en" ? "en" : "ko";
        var languageChanged = newLanguage != _language;
        _language = newLanguage;
        _translateSource = NormalizeTranslationLang(SettingsTranslateSource, "Korean");
        _translateTarget = NormalizeTranslationLang(SettingsTranslateTarget, "English");
        _translator = CreateTranslator(_ollamaEndpoint, _ollamaModel, _translateSource, _translateTarget);
        SettingsStatus = languageChanged ? L("L.SettingsSavedRestart") : L("L.SettingsSaved");
        SaveState();
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    /// <summary>번역 언어 값을 선택지(영어 표기)로 정규화. 미상이면 기본값.</summary>
    private string NormalizeTranslationLang(string? value, string fallback)
    {
        var v = (value ?? "").Trim();
        return AvailableTranslationLanguages.Any(l => string.Equals(l.Id, v, StringComparison.OrdinalIgnoreCase))
            ? AvailableTranslationLanguages.First(l => string.Equals(l.Id, v, StringComparison.OrdinalIgnoreCase)).Id
            : fallback;
    }

}
