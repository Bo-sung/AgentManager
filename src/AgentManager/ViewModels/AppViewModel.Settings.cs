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
using AgentManager.Core;
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
    private string _settingsPiPath = "";
    public string SettingsPiPath { get => _settingsPiPath; set => Set(ref _settingsPiPath, value); }
    private string _settingsPiWorkerPath = "";
    /// <summary>pi-worker 런처 경로(편집기 값). 빈 값 = 자동 탐지. Worker 역할 pi 세션에만 적용.</summary>
    public string SettingsPiWorkerPath { get => _settingsPiWorkerPath; set => Set(ref _settingsPiWorkerPath, value); }
    private string _settingsOllamaEndpoint = "";
    public string SettingsOllamaEndpoint { get => _settingsOllamaEndpoint; set { if (Set(ref _settingsOllamaEndpoint, value)) OnChanged(nameof(OllamaEndpointRemote)); } }
    private bool _settingsAllowRemoteOllama;
    /// <summary>SEC (egress guard): opt-in to let the built-in Ollama translator target a non-loopback host.
    /// Persisted to <see cref="Core.Settings.SettingsService.AllowRemoteOllamaEndpoint"/>.</summary>
    public bool SettingsAllowRemoteOllama { get => _settingsAllowRemoteOllama; set => Set(ref _settingsAllowRemoteOllama, value); }
    /// <summary>True when the edited Ollama endpoint is NOT a loopback host — drives the egress warning banner
    /// + opt-in checkbox in Settings. Computed (get-only ⇒ bind OneWay).</summary>
    public bool OllamaEndpointRemote => !Core.Translation.OllamaTranslator.IsLoopbackEndpoint(SettingsOllamaEndpoint);
    private string _settingsOllamaModel = "";
    public string SettingsOllamaModel { get => _settingsOllamaModel; set => Set(ref _settingsOllamaModel, value); }
    private int _settingsOllamaTimeoutSeconds = 60;
    /// <summary>Ollama 번역 타임아웃(초) — 편집기 값. 큰 모델이 60초를 넘겨 조용히 원문 폴백되는 문제를 위해 선택 가능.</summary>
    public int SettingsOllamaTimeoutSeconds { get => _settingsOllamaTimeoutSeconds; set => Set(ref _settingsOllamaTimeoutSeconds, value); }
    /// <summary>타임아웃 선택지(초) — 설정 ComboBox 바인딩용. 재시도는 선택값의 2배까지 늘어남.</summary>
    public int[] OllamaTimeoutOptions { get; } = [30, 60, 120, 180, 300, 600];

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
            OnChanged(nameof(OllamaStatusText)); OnChanged(nameof(OllamaDown));
        }
    }
    public bool OllamaRunning => _ollamaState == "running";
    public bool OllamaStopped => _ollamaState == "stopped";   // 설치됐지만 꺼짐
    public bool OllamaAbsent => _ollamaState == "absent";     // 미설치
    /// <summary>Ollama가 확실히 불가(꺼짐/미설치) — Ollama 설정 패널 상태 표시용. checking/unknown은 제외.</summary>
    public bool OllamaDown => _ollamaState is "stopped" or "absent";

    // ----- 번역 provider 준비 상태 (provider-agnostic: ollama / agent:<id> / custom endpoint) -----
    // 번역 ON 게이팅은 선택된 provider의 가용성에 따른다(과거엔 Ollama 프로세스 상태에 하드코딩되어, agent/custom
    // provider를 골라도 Ollama가 켜져 있어야만 번역을 켤 수 있었음). IsTranslatorReadyAsync는 저장된 _settings의
    // 선택 provider를 검사하므로, 여기 캐시된 신호는 메인 창의 토글/경고 아이콘 어포던스에 그대로 쓰인다.
    private bool _translationReady;
    private bool _translationChecked;
    /// <summary>선택된 번역 provider가 준비됨(도달 가능/설치됨) — 번역 토글 활성 판단.</summary>
    public bool TranslationReady
    {
        get => _translationReady;
        private set { if (Set(ref _translationReady, value)) { OnChanged(nameof(CanTranslate)); OnChanged(nameof(TranslationProviderDown)); } }
    }
    /// <summary>번역 레이어 사용 가능 여부 = 선택된 provider 준비됨.</summary>
    public bool CanTranslate => TranslationReady;
    /// <summary>선택된 provider가 확실히 불가 — 번역 토글 옆 경고 아이콘 표시용. 미검사(unknown)는 제외.</summary>
    public bool TranslationProviderDown => _translationChecked && !_translationReady;
    /// <summary>선택된 번역 provider의 준비 상태를 재확인(provider-agnostic). 저장된 설정 기준.</summary>
    public async Task RefreshTranslationStatusAsync()
    {
        var ready = await IsTranslatorReadyAsync();
        _translationChecked = true;
        TranslationReady = ready;
    }
    public string OllamaStatusText => _ollamaState switch
    {
        "running" => App.L("L.OllamaRunning"),
        "stopped" => App.L("L.OllamaStopped"),
        "absent" => App.L("L.OllamaAbsent"),
        "checking" => App.L("L.OllamaChecking"),
        _ => "",
    };

    /// <summary>Ollama 상태 갱신: 핑 성공=running, 실패면 설치 여부로 stopped/absent 구분.</summary>
    public async Task RefreshOllamaStatusAsync()
    {
        OllamaState = "checking";
        var endpoint = string.IsNullOrWhiteSpace(SettingsOllamaEndpoint) ? _ollamaEndpoint : SettingsOllamaEndpoint;
        var up = await Core.Translation.OllamaTranslator.PingAsync(endpoint, 1500);
        OllamaState = up ? "running" : (EngineRegistry.OllamaExe() is not null ? "stopped" : "absent");
    }

    // The ollama process WE started via the settings '실행' button (null = we never launched one). We only ever
    // stop THIS reference — an ollama the user runs themselves (desktop app / service / terminal) is external and
    // must never be touched. Used to reap our own child on exit/update so it can't orphan and lock the install dir.
    private System.Diagnostics.Process? _ollamaProcess;

    /// <summary>설정의 '실행' 버튼 — 설치된 ollama를 'serve'로 백그라운드 기동 후 상태 재확인.</summary>
    public void StartOllama()
    {
        var exe = EngineRegistry.OllamaExe();
        if (exe is null) { OllamaState = "absent"; return; }
        try
        {
            // Explicit WorkingDirectory (never inherit our cwd) — ollama serve is long-running and orphans when we
            // close; if it inherited …\current\ it would hold the install folder open and block self-update.
            _ollamaProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            });
        }
        catch { }
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 6 && !await Core.Translation.OllamaTranslator.PingAsync(_ollamaEndpoint, 1500); i++) await Task.Delay(1000);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await RefreshOllamaStatusAsync());
        });
    }

    /// <summary>Terminate ONLY the ollama process we started ourselves (the '실행' button), tree included. No-op
    /// when we never launched one, or it already exited. An externally-run ollama is never referenced here, so it
    /// is never affected. Called on app shutdown and before a self-update so our child can't orphan and hold the
    /// install folder open.</summary>
    internal void StopSpawnedOllama()
    {
        var p = _ollamaProcess;
        _ollamaProcess = null;
        try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        try { p?.Dispose(); } catch { }
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
    private string _translateSource { get => _settings.TranslateSourceLanguage; set => _settings.TranslateSourceLanguage = value; }
    private string _translateTarget { get => _settings.TranslateTargetLanguage; set => _settings.TranslateTargetLanguage = value; }
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
    private bool _warnNoWorktree { get => _settings.WarnNoWorktree; set => _settings.WarnNoWorktree = value; }

    /// <summary>새 세션 기본 승인 정책: ask | safe | yolo. RequireApproval + Sandbox 둘 다 시드.</summary>
    private string _approvalPolicy { get => _settings.ApprovalPolicy; set => _settings.ApprovalPolicy = value; }
    private string _settingsApprovalPolicy = "yolo";
    public string SettingsApprovalPolicy { get => _settingsApprovalPolicy; set => Set(ref _settingsApprovalPolicy, value); }

    // ----- orchestration -----
    private string _worktreeBase { get => _settings.WorktreeBase; set => _settings.WorktreeBase = value; }
    public string SettingsWorktreeBase { get => _settingsWorktreeBase; set => Set(ref _settingsWorktreeBase, value); }
    private string _settingsWorktreeBase = "";
    private bool _autoStartLastSession { get => _settings.AutoStartLastSession; set => _settings.AutoStartLastSession = value; }
    public bool SettingsAutoStart { get => _settingsAutoStart; set => Set(ref _settingsAutoStart, value); }
    private bool _settingsAutoStart;
    /// <summary>목록의 실시간 활동 표시 여부 (즉시 반영, 영속).</summary>
    public bool StreamLogs { get => _settings.StreamLogs; set { if (_settings.StreamLogs != value) { _settings.StreamLogs = value; OnChanged(nameof(StreamLogs)); } } }
    public bool SettingsStreamLogs { get => _settingsStreamLogs; set => Set(ref _settingsStreamLogs, value); }
    private bool _settingsStreamLogs = true;

    // ----- per-engine default model -----
    private Dictionary<string, string> _defaultModels { get => _settings.DefaultModels; set => _settings.DefaultModels = value; }
    public string[] CcModels => DropdownModelsFor("cc");
    public string[] GxModels => DropdownModelsFor("gx");
    public string[] AgyModels => DropdownModelsFor("agy");
    public string[] PiModels => DropdownModelsFor("pi");
    // 엔진 모델 목록은 엔진별 config(engines/*.json)에서 — 사용자가 파일 편집으로 추가 가능. 미로드 시 카탈로그 폴백(P1c).
    private string[] EngineModels(string id) => _engineConfig?.Get(id) is { } c ? [.. c.ModelIds()] : [.. _modelCatalog.ModelsFor(id)];

    // ----- "주로 쓰는 모델" — 엔진별 선호 집합(설정 영속) + 체크리스트(cc/gx/agy/pi 공통) -----
    private Dictionary<string, HashSet<string>> _preferred => _settings.Preferred;
    public ModelChecklistVm CcChecklist { get; private set; } = null!;
    public ModelChecklistVm GxChecklist { get; private set; } = null!;
    public ModelChecklistVm AgyChecklist { get; private set; } = null!;
    public ModelChecklistVm PiChecklist { get; private set; } = null!;

    /// <summary>체크리스트 인스턴스 생성(생성자에서 1회). 모델 목록은 설정 열 때 SetModels로 채운다.</summary>
    private void InitModelChecklists()
    {
        CcChecklist = new("cc", _preferred["cc"], showFilter: false, () => RefreshEngineModels("cc"));
        GxChecklist = new("gx", _preferred["gx"], showFilter: false, () => RefreshEngineModels("gx"));
        AgyChecklist = new("agy", _preferred["agy"], showFilter: false, () => RefreshEngineModels("agy"));
        PiChecklist = new("pi", _preferred["pi"], showFilter: true, () => RefreshEngineModels("pi"));
    }
    /// <summary>cc/gx/agy 체크리스트를 정적 모델로 채운다(설정 열 때). pi는 카탈로그 조회 후 채워짐.</summary>
    private void SeedStaticChecklists()
    {
        CcChecklist.SetModels(EngineModels("cc"));
        GxChecklist.SetModels(EngineModels("gx"));
        AgyChecklist.SetModels(EngineModels("agy"));
    }
    private void RefreshEngineModels(string id)
    {
        var list = DropdownModelsFor(id);
        // 현재 선택한 기본 모델이 좁혀진 목록에 없으면 첫 항목으로 보정(드롭다운 공백 방지).
        void Coerce(string cur, Action<string> set) { if (!string.IsNullOrEmpty(cur) && list.Length > 0 && Array.IndexOf(list, cur) < 0) set(list[0]); }
        switch (id)
        {
            case "cc": Coerce(SettingsModelCc, v => SettingsModelCc = v); OnChanged(nameof(CcModels)); break;
            case "gx": Coerce(SettingsModelGx, v => SettingsModelGx = v); OnChanged(nameof(GxModels)); break;
            case "agy": Coerce(SettingsModelAgy, v => SettingsModelAgy = v); OnChanged(nameof(AgyModels)); break;
            default: Coerce(SettingsModelPi, v => SettingsModelPi = v); OnChanged(nameof(PiModels)); break;
        }
        OnChanged(nameof(NewAgentModels));
        NotifySessionModelsChanged(id); // 이 엔진 세션들의 컴포저 모델 목록도 즉시 갱신(체크 변경 반영)
    }

    /// <summary>드롭다운(피커/설정)에 노출할 엔진 모델 — 체크된 게 있으면 그 부분집합, 없으면 전체.
    /// pi는 동적 카탈로그를 쓰고 "default"(=~/.pi 기본값)를 항상 앞에 둔다.</summary>
    public string[] DropdownModelsFor(string id)
    {
        var pref = _preferred.GetValueOrDefault(id);
        if (id == "pi")
        {
            if (pref is { Count: > 0 }) return ["default", .. pref.OrderBy(m => m, StringComparer.OrdinalIgnoreCase)];
            return _piCatalog.Count > 0 ? [.. _piCatalog] : EngineModels("pi");
        }
        // agy는 `agy models` 동적 카탈로그(로드됐으면)를 기반으로, 없으면 정적 목록. "default"(--model 생략)는 항상 앞에.
        var all = id == "agy" && _agyCatalog.Count > 0 ? ["default", .. _agyCatalog] : EngineModels(id);
        if (pref is not { Count: > 0 }) return all;
        // custom = 사용자가 설정에서 추가한 세부 버전(정적 별칭 목록에 없는 것) → 항상 드롭다운에 더한다.
        var custom = pref.Where(m => !all.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray();
        var picked = all.Where(pref.Contains).ToArray();       // 정적 중 명시 선택(필터); 없으면 별칭 전부 유지
        var staticPart = picked.Length > 0 ? picked : all;
        return custom.Length > 0 ? [.. staticPart, .. custom] : staticPart;
    }

    // pi: 동적 모델 카탈로그(`pi --list-models`) + 연동 provider.
    private readonly List<string> _piCatalog = [];
    private IReadOnlyList<string> _piProviders = [];
    private bool _piCatalogLoaded;
    public string PiConnectedProviders => _piProviders.Count > 0
        ? App.L("L.PiConnected", string.Join(" · ", _piProviders))
        : App.L("L.PiConnectedNone");
    private string _piModelsStatus = "";
    public string PiModelsStatus { get => _piModelsStatus; private set => Set(ref _piModelsStatus, value); }

    /// <summary>`pi --list-models`로 모델/연동 provider를 조회(첫 호출만; force로 강제 새로고침).</summary>
    public async Task QueryPiModelsAsync(bool force = false)
    {
        if (_piCatalogLoaded && !force) return;
        _piCatalogLoaded = true;
        PiModelsStatus = App.L("L.Querying");
        try
        {
            var cat = await EngineRegistry.QueryPiCatalogAsync(_piPath);
            _piCatalog.Clear();
            _piCatalog.AddRange(cat.Models);
            _piProviders = cat.Providers;
            _engineConfig?.UpdateModelsFromQuery("pi", cat.Models); // 조회 결과가 다르면 engines/pi.json 모델 목록 갱신
            PiChecklist.SetModels(_piCatalog);
            PiModelsStatus = cat.Models.Count > 0 ? App.L("L.ModelsFound", cat.Models.Count) : App.L("L.NoModels");
        }
        catch { _piCatalogLoaded = false; PiModelsStatus = App.L("L.QueryFailed"); }
        OnChanged(nameof(PiModels));
        OnChanged(nameof(PiConnectedProviders));
        OnChanged(nameof(NewAgentModels));
        NotifySessionModelsChanged("pi");
    }

    /// <summary>엔진의 모델 목록이 (재)로드/필터되면 열려있는 컴포저 모델 메뉴가 새 목록을 반영하도록 그 엔진 세션 바인딩을 갱신.</summary>
    private void NotifySessionModelsChanged(string engineId)
    {
        foreach (var s in _allSessions.Where(s => s.AgentId == engineId)) s.RaiseAvailableModelsChanged();
    }

    // agy: `agy models` 동적 카탈로그 — 하드코딩 대체(라벨 그대로 --model). pi 조회와 동일 패턴.
    private readonly List<string> _agyCatalog = [];
    private bool _agyCatalogLoaded;
    private string _agyModelsStatus = "";
    public string AgyModelsStatus { get => _agyModelsStatus; private set => Set(ref _agyModelsStatus, value); }

    /// <summary>`agy models`로 모델 목록을 조회(첫 호출만; force로 강제 새로고침) — cc/gx는 CLI 목록 커맨드가 없어 조회 불가.</summary>
    public async Task QueryAgyModelsAsync(bool force = false)
    {
        if (_agyCatalogLoaded && !force) return;
        _agyCatalogLoaded = true;
        AgyModelsStatus = App.L("L.Querying");
        try
        {
            var models = await EngineRegistry.QueryAgyModelsAsync(_agyPath);
            _agyCatalog.Clear();
            _agyCatalog.AddRange(models);
            if (models.Count > 0) _engineConfig?.UpdateModelsFromQuery("agy", ["default", .. models]); // engines/agy.json 모델 목록 갱신
            AgyChecklist.SetModels(_agyCatalog.Count > 0 ? ["default", .. _agyCatalog] : EngineModels("agy"));
            AgyModelsStatus = models.Count > 0 ? App.L("L.ModelsFound", models.Count) : App.L("L.NoModels");
        }
        catch { _agyCatalogLoaded = false; AgyModelsStatus = App.L("L.QueryFailed"); }
        OnChanged(nameof(AgyModels));
        OnChanged(nameof(NewAgentModels));
        NotifySessionModelsChanged("agy");
    }

    /// <summary>엔진의 기본 모델 (engines/&lt;id&gt;.json → 없으면 첫 모델; 스토어 미로드 시 레거시 폴백).</summary>
    private string DefaultModelFor(string id)
    {
        var m = _engineConfig?.Get(id) is { } c ? c.DefaultModel
            : (_defaultModels.TryGetValue(id, out var lm) ? lm : null);
        return !string.IsNullOrWhiteSpace(m) ? m! : (EngineModels(id).FirstOrDefault() ?? "");
    }
    /// <summary>기본 모델을 engines/&lt;id&gt;.json에 저장 (유효한 모델만; pi는 "provider/id" 자유형식).</summary>
    private void SetDefaultModel(string id, string model)
    {
        // pi는 멀티 provider — 자유형식 허용("default"/빈값은 ~/.pi 기본값 사용); 그 외는 목록에 있을 때만.
        string? val = id == "pi"
            ? (!string.IsNullOrWhiteSpace(model) && model != "default" ? model : null)
            : (!string.IsNullOrWhiteSpace(model) && Array.IndexOf(EngineModels(id), model) >= 0 ? model : null);
        if (_engineConfig?.Get(id) is { } c) _engineConfig.Upsert(c with { DefaultModel = val });
        else if (val is null) _defaultModels.Remove(id);
        else _defaultModels[id] = val;
    }
    private static readonly string[] PreferredEngines = ["cc", "gx", "agy", "pi"];

    /// <summary>Load the checklist working set (_preferred[id]) from the engine config's preferred flags — engines/*.json
    /// is authoritative. Called once the store is loaded; the checklists bind to these same HashSets.</summary>
    private void SyncPreferredFromStore()
    {
        foreach (var id in PreferredEngines)
        {
            if (_engineConfig?.Get(id) is not { } c || !_preferred.TryGetValue(id, out var set)) continue;
            set.Clear();
            foreach (var m in c.PreferredModelIds()) set.Add(m);
        }
    }

    /// <summary>Persist the checklist working set (_preferred[id]) back to engines/&lt;id&gt;.json — flips each model's
    /// preferred flag and adds any custom (checklist-typed) model not yet in the list.</summary>
    private void SyncPreferredToStore()
    {
        foreach (var id in PreferredEngines)
        {
            if (_engineConfig?.Get(id) is not { } c) continue;
            var pref = _preferred.GetValueOrDefault(id) ?? [];
            var models = c.ModelList.Select(m => m with { Preferred = pref.Contains(m.Id) }).ToList();
            var known = new HashSet<string>(models.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var m in pref) if (!known.Contains(m)) models.Add(new AgentManager.Core.Engines.EngineModelConfig(m, Preferred: true));
            _engineConfig.Upsert(c with { Models = models });
        }
    }

    /// <summary>Load the auth service (mode / encrypted api key / auto-api) from engines/*.json — authoritative —
    /// while preserving the runtime rate-limit cooldowns already in the service.</summary>
    private void SyncAuthFromStore()
    {
        if (_engineConfig is null) return;
        var mode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var apiKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var autoApi = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in PreferredEngines)
        {
            if (_engineConfig.Get(id) is not { } c) continue;
            var a = c.AuthOrDefault;
            mode[id] = a.Mode;
            if (!string.IsNullOrEmpty(a.ApiKeyEnc)) apiKey[id] = a.ApiKeyEnc;
            autoApi[id] = a.AutoApiOnLimit;
        }
        _engineAuth.Load(mode, apiKey, autoApi, _engineAuth.SnapshotLimitedUntil());
    }

    /// <summary>Persist the auth service state (mode / encrypted key / auto-api) back to engines/&lt;id&gt;.json.</summary>
    private void SyncAuthToStore()
    {
        if (_engineConfig is null) return;
        var keys = _engineAuth.SnapshotApiKey();
        foreach (var id in PreferredEngines)
        {
            if (_engineConfig.Get(id) is not { } c) continue;
            _engineConfig.Upsert(c with
            {
                Auth = new AgentManager.Core.Engines.EngineAuthConfig(
                    _engineAuth.GetAuthMode(id),
                    keys.TryGetValue(id, out var k) ? k : "",
                    _engineAuth.GetAutoApi(id)),
            });
        }
    }

    /// <summary>Load per-engine enabled + skill-dir from engines/*.json into the runtime working copies
    /// (_disabledEngines / _skillDirs) — engines/*.json is authoritative.</summary>
    private void SyncEngineFlagsFromStore()
    {
        if (_engineConfig is null) return;
        _disabledEngines.Clear();
        foreach (var id in PreferredEngines)
        {
            if (_engineConfig.Get(id) is not { } c) continue;
            if (!c.Enabled) _disabledEngines.Add(id);
            if (!string.IsNullOrWhiteSpace(c.SkillDir)) _skillDirs[id] = c.SkillDir;
        }
    }

    /// <summary>Persist per-engine enabled + skill-dir back to engines/&lt;id&gt;.json.</summary>
    private void SyncEngineFlagsToStore()
    {
        if (_engineConfig is null) return;
        foreach (var id in PreferredEngines)
        {
            if (_engineConfig.Get(id) is not { } c) continue;
            _engineConfig.Upsert(c with
            {
                Enabled = !_disabledEngines.Contains(id),
                SkillDir = _skillDirs.GetValueOrDefault(id, ""),
            });
        }
    }

    public string SettingsModelCc { get => _settingsModelCc; set => Set(ref _settingsModelCc, value); }
    private string _settingsModelCc = "";
    public string SettingsModelGx { get => _settingsModelGx; set => Set(ref _settingsModelGx, value); }
    private string _settingsModelGx = "";
    public string SettingsModelAgy { get => _settingsModelAgy; set => Set(ref _settingsModelAgy, value); }
    private string _settingsModelAgy = "";
    public string SettingsModelPi { get => _settingsModelPi; set => Set(ref _settingsModelPi, value); }
    private string _settingsModelPi = "";

    // ----- per-engine enable/disable -----
    public bool SettingsEngineCc { get => _settingsEngineCc; set => Set(ref _settingsEngineCc, value); }
    private bool _settingsEngineCc = true;
    public bool SettingsEngineGx { get => _settingsEngineGx; set => Set(ref _settingsEngineGx, value); }
    private bool _settingsEngineGx = true;
    public bool SettingsEngineAgy { get => _settingsEngineAgy; set => Set(ref _settingsEngineAgy, value); }
    private bool _settingsEngineAgy = true;
    public bool SettingsEnginePi { get => _settingsEnginePi; set => Set(ref _settingsEnginePi, value); }
    private bool _settingsEnginePi = true;

    // ----- per-engine auth (subscription / api key) → Core EngineAuthService (overhaul (a) step 2) -----
    // 상태(4 dict)와 로직은 Core 서비스가 소유; VM은 forward만 한다(호출부 불변, 서비스가 실제로 쓰인다).
    // DPAPI·usage는 델리게이트로 주입 → Core는 WPF/Windows-포트 비의존. usageOf는 _usageService를 lazy로 읽는다.
    private AgentManager.Core.Settings.EngineAuthService? _engineAuthBacking;
    private AgentManager.Core.Settings.EngineAuthService _engineAuth => _engineAuthBacking ??=
        new(Persistence.Dpapi.Encrypt, Persistence.Dpapi.Decrypt,
            id => _usageService.TryGet(id, out var u) ? (u.Utilization, u.ResetsAtUnix) : ((double, long)?)null);

    public bool HasApiKey(string id) => _engineAuth.HasApiKey(id);
    public bool AutoApiOnLimit(string id) => _engineAuth.AutoApiOnLimit(id);
    public bool IsEngineLimited(string id) => _engineAuth.IsEngineLimited(id);
    public bool WillUseApiOnLimit(string id) => _engineAuth.WillUseApiOnLimit(id);
    public void MarkRateLimited(string id, long resetUnix) => _engineAuth.MarkRateLimited(id, resetUnix);
    private IReadOnlyDictionary<string, string> ApiEnvFor(string id) => _engineAuth.ApiEnvFor(id);
    private void SaveEngineAuth(string id, string mode, string plainKey) => _engineAuth.SaveEngineAuth(id, mode, plainKey);
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
    // ----- agy auth (API 모드 = Antigravity SDK 백엔드; subscription = 기존 CLI/ConPTY) -----
    public string SettingsAuthAgy { get => _settingsAuthAgy; set => Set(ref _settingsAuthAgy, value); }
    private string _settingsAuthAgy = "subscription";
    public string SettingsApiKeyAgy { get => _settingsApiKeyAgy; set => Set(ref _settingsApiKeyAgy, value); }
    private string _settingsApiKeyAgy = "";
    private bool _settingsAutoApiAgy;
    public bool SettingsAutoApiAgy { get => _settingsAutoApiAgy; set => Set(ref _settingsAutoApiAgy, value); }

    /// <summary>이 엔진이 현재 API 모드로 동작해야 하는가 — 백엔드 분기(agy 어댑터 선택)의 단일 기준.
    /// 명시적 API 모드 + 키 보유, 또는 (한도 소진 + 자동전환 ON + 키 보유). cc/gx는 어댑터 내 크리덴셜만
    /// 바꾸지만 agy는 어댑터 클래스 자체를 바꾸므로 이 값이 AgyAdapter ↔ AgySdkAdapter를 결정한다.</summary>
    public bool IsApiMode(string id) => _engineAuth.IsApiMode(id);

    // ----- appearance: accent / zoom / telemetry -----
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
    public bool IsModalActive => ShowNewAgent || ShowWorkerAssign || ShowNoIdleWorker || ShowAbout || ShowNewProject || ShowNewSchedule || ShowInstallGuide || ShowTrustPrompt;

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
    private bool _telemetry { get => _settings.Telemetry; set => _settings.Telemetry = value; }
    public bool SettingsTelemetry { get => _settingsTelemetry; set => Set(ref _settingsTelemetry, value); }
    private bool _settingsTelemetry;

    private static (bool requireApproval, SandboxMode sandbox) PolicyToSession(string policy) => CoreHelpers.PolicyToSession(policy);

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
    public string PiDetectLabel => DetectLabel("pi", _piPath);
    /// <summary>pi-worker 경로 상태 라벨(설정 화면). worker 해석 경로를 그대로 반영.</summary>
    public string PiWorkerDetectLabel
    {
        get
        {
            var exe = EngineRegistry.ResolveExe("pi", piWorker: true, piWorkerPath: _piWorkerPath);
            if (exe is null) return AgentManager.App.L("L.DetectMissing");
            if (File.Exists(exe)) return AgentManager.App.L("L.DetectPathPrefix", exe);
            return AgentManager.App.L("L.DetectPathDependent", exe);
        }
    }
    private static string DetectLabel(string id, string? overridePath)
    {
        var exe = EngineRegistry.ResolveExe(id,
            id == "cc" ? overridePath : null,
            id == "gx" ? overridePath : null,
            id == "agy" ? overridePath : null,
            id == "pi" ? overridePath : null);
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
            case "pi": SettingsPiPath = found; break;
        }
        SettingsStatus = AgentManager.App.L("L.DetectFound", found);
    }

    /// <summary>'탐지' 버튼(pi-worker) — npm 전역 하네스만 오토 탐지해 경로란에 채운다(못 찾으면 입력값 보존).</summary>
    public void DetectPiWorkerPath()
    {
        var found = EngineRegistry.DetectPiWorkerExe();
        if (found is null) { SettingsStatus = AgentManager.App.L("L.DetectFailed"); return; }
        SettingsPiWorkerPath = found;
        SettingsStatus = AgentManager.App.L("L.DetectFound", found);
    }
    private void RefreshDetectLabels()
    {
        OnChanged(nameof(ClaudeDetectLabel)); OnChanged(nameof(CodexDetectLabel));
        OnChanged(nameof(AgyDetectLabel)); OnChanged(nameof(PiDetectLabel)); OnChanged(nameof(PiWorkerDetectLabel));
        OnChanged(nameof(CcAccount)); OnChanged(nameof(GxAccount));
        OnChanged(nameof(AgyAccount));
    }

    // 각 CLI의 로그인 계정(구독 인증) — 이메일, 미로그인 시 "". UI: 비어있으면 Sign in, 있으면 계정 표시.
    public string CcAccount => Persistence.EngineAccounts.For("cc") ?? "";
    public string GxAccount => Persistence.EngineAccounts.For("gx") ?? "";
    public string AgyAccount => Persistence.EngineAccounts.For("agy") ?? "";
    private string _settingsStatus = "";
    public string SettingsStatus { get => _settingsStatus; set => Set(ref _settingsStatus, value); }

    // ----- 스킬 주입: Save 시 각 엔진 스킬 폴더에 SKILL.md 기록 -----
    private string _skillContent { get => _settings.SkillContent; set => _settings.SkillContent = value; }
    private Dictionary<string, string> _skillDirs { get => _settings.SkillDirs; set => _settings.SkillDirs = value; }

    private string _settingsSkillContent = "";
    public string SettingsSkillContent { get => _settingsSkillContent; set => Set(ref _settingsSkillContent, value); }
    private string _settingsSkillDirCc = "";
    public string SettingsSkillDirCc { get => _settingsSkillDirCc; set => Set(ref _settingsSkillDirCc, value); }
    private string _settingsSkillDirGx = "";
    public string SettingsSkillDirGx { get => _settingsSkillDirGx; set => Set(ref _settingsSkillDirGx, value); }
    private string _settingsSkillDirAgy = "";
    public string SettingsSkillDirAgy { get => _settingsSkillDirAgy; set => Set(ref _settingsSkillDirAgy, value); }
    private string _settingsSkillDirPi = "";
    public string SettingsSkillDirPi { get => _settingsSkillDirPi; set => Set(ref _settingsSkillDirPi, value); }

    private string _skillInjectStatus = "";
    /// <summary>마지막 Save 시 엔진별 스킬 주입 결과 요약 (스킬 카드에 표시).</summary>
    public string SkillInjectStatus { get => _skillInjectStatus; set => Set(ref _skillInjectStatus, value); }

    private static Dictionary<string, string> MergeSkillDirs(IReadOnlyDictionary<string, string>? saved)
    {
        var dirs = AgentManager.Core.SkillInjector.DefaultDirs();
        if (saved != null)
            foreach (var kv in saved)
                if (!string.IsNullOrWhiteSpace(kv.Value)) dirs[kv.Key] = kv.Value;
        return dirs;
    }

    /// <summary>편집기 버퍼 → 필드로 반영하고 각 엔진 스킬 폴더에 SKILL.md를 기록. 결과를 상태로 요약.</summary>
    private void ApplyAndInjectSkill()
    {
        _skillContent = string.IsNullOrWhiteSpace(SettingsSkillContent)
            ? AgentManager.Core.SkillInjector.WorkerPromptDefault
            : SettingsSkillContent;
        _skillDirs = new Dictionary<string, string>
        {
            ["cc"] = (SettingsSkillDirCc ?? "").Trim(),
            ["gx"] = (SettingsSkillDirGx ?? "").Trim(),
            ["agy"] = (SettingsSkillDirAgy ?? "").Trim(),
            ["pi"] = (SettingsSkillDirPi ?? "").Trim(),
        };
        var results = AgentManager.Core.SkillInjector.Inject(_skillContent, _skillDirs);
        // 구조화 ask-user 스킬도 함께 주입(별도 폴더 <dir>/ask-user/ — worker-prompt와 충돌 없음)
        AgentManager.Core.SkillInjector.Inject(AgentManager.Core.SkillInjector.AskUserDefault, _skillDirs);
        var parts = results.Select(kv => $"{kv.Key} {(kv.Value is null ? "✓" : "✗")}");
        var firstError = results.Where(kv => kv.Value is not null).Select(kv => $"{kv.Key}: {kv.Value}").FirstOrDefault();
        SkillInjectStatus = string.Join("   ", parts) + (firstError is null ? "" : $"   — {firstError}");
    }

    /// <summary>VM 필드 → 설정 에디터(Settings* 미러) 프로퍼티로 끌어온다. OpenSettings + 외부 리로드에서 공용.</summary>
    // ---- translation provider picker (Ollama / installed agents / custom OpenAI-compat) ----

    /// <summary>Selectable providers for the settings picker (built-ins + custom). Rebuilt on open / edit.</summary>
    public ObservableCollection<Core.Translation.TranslationProviderInfo> TranslationProviders { get; } = [];

    private string _settingsTranslationSelectedId = "ollama";
    public string SettingsTranslationSelectedId
    {
        get => _settingsTranslationSelectedId;
        set { if (Set(ref _settingsTranslationSelectedId, value)) { OnChanged(nameof(IsAgentProviderSelected)); OnChanged(nameof(IsCustomProviderSelected)); OnChanged(nameof(TranslationAgentModels)); } }
    }

    /// <summary>True when the selected provider is an installed agent (agent:&lt;id&gt;) — gates the agent model picker.</summary>
    public bool IsAgentProviderSelected => _settingsTranslationSelectedId?.StartsWith("agent:", StringComparison.Ordinal) == true;

    /// <summary>True when the selected provider is a custom (non-local) endpoint — shows the egress warning.</summary>
    public bool IsCustomProviderSelected => _settingsTranslationSelectedId?.StartsWith("custom:", StringComparison.Ordinal) == true;

    /// <summary>Models offered by the selected agent for the translation-model picker — the SAME list the rest of
    /// the app uses (<see cref="DropdownModelsFor"/>: built-in + the user's custom-added / preferred versions), so
    /// it stays consistent with the composer / new-session model menus. Empty for non-agent providers.</summary>
    public string[] TranslationAgentModels =>
        IsAgentProviderSelected ? DropdownModelsFor(_settingsTranslationSelectedId["agent:".Length..]) : [];

    private string _settingsTranslationAgentModel = "";
    /// <summary>Chosen translation model for the agent provider (blank = engine default).</summary>
    public string SettingsTranslationAgentModel { get => _settingsTranslationAgentModel; set => Set(ref _settingsTranslationAgentModel, value); }

    /// <summary>Editable copy of the custom OpenAI-compat entries (applied to settings on Save).</summary>
    public ObservableCollection<Core.Translation.TranslationCustomProvider> CustomTranslationProviders { get; } = [];

    private string _newCustomName = "", _newCustomEndpoint = "", _newCustomModel = "", _newCustomKey = "";
    public string NewCustomName { get => _newCustomName; set => Set(ref _newCustomName, value); }
    public string NewCustomEndpoint { get => _newCustomEndpoint; set { if (Set(ref _newCustomEndpoint, value)) OnChanged(nameof(IsNewCustomEndpointRemote)); } }

    /// <summary>True when the entered endpoint is non-loopback (prompts would leave this machine) — shows a warning.
    /// The Core egress guard additionally REFUSES to send to a non-loopback plaintext-HTTP endpoint.</summary>
    public bool IsNewCustomEndpointRemote => Core.Translation.TranslationEndpointPolicy.IsRemote(_newCustomEndpoint);
    public string NewCustomModel { get => _newCustomModel; set => Set(ref _newCustomModel, value); }
    public string NewCustomKey { get => _newCustomKey; set => Set(ref _newCustomKey, value); }

    private RelayCommand? _addCustomProviderCommand;
    public RelayCommand AddCustomProviderCommand => _addCustomProviderCommand ??= new RelayCommand(_ => AddCustomProvider());

    private RelayCommand? _removeCustomProviderCommand;
    public RelayCommand RemoveCustomProviderCommand => _removeCustomProviderCommand ??= new RelayCommand(p =>
    {
        if (p is Core.Translation.TranslationCustomProvider cp)
        {
            CustomTranslationProviders.Remove(cp);
            if (SettingsTranslationSelectedId == cp.Id) SettingsTranslationSelectedId = "ollama";
            RebuildTranslationProviders();
        }
    });

    private void AddCustomProvider()
    {
        var endpoint = NewCustomEndpoint.Trim();
        if (endpoint.Length == 0) return;
        var cp = new Core.Translation.TranslationCustomProvider
        {
            Id = "custom:" + Guid.NewGuid().ToString("N")[..8],
            Name = NewCustomName.Trim(),
            Endpoint = endpoint,
            Model = NewCustomModel.Trim(),
            // Key is DPAPI-encrypted at rest (CurrentUser); opaque to Core.
            ApiKeyEnc = string.IsNullOrWhiteSpace(NewCustomKey) ? "" : Persistence.Dpapi.Encrypt(NewCustomKey.Trim()),
        };
        CustomTranslationProviders.Add(cp);
        RebuildTranslationProviders();
        SettingsTranslationSelectedId = cp.Id; // auto-select the just-added entry
        NewCustomName = NewCustomEndpoint = NewCustomModel = NewCustomKey = "";
    }

    private void LoadCustomProvidersEditor()
    {
        CustomTranslationProviders.Clear();
        foreach (var c in _settings.TranslationCustomProviders)
            CustomTranslationProviders.Add(new Core.Translation.TranslationCustomProvider
            { Id = c.Id, Name = c.Name, Endpoint = c.Endpoint, Model = c.Model, ApiKeyEnc = c.ApiKeyEnc });
    }

    private void RebuildTranslationProviders()
    {
        TranslationProviders.Clear();
        TranslationProviders.Add(new("ollama", "ollama", "Ollama (local)"));
        foreach (var id in Core.Translation.AgentTranslator.SupportedEngines)
            if (!string.IsNullOrWhiteSpace(ResolveTranslatorExe(id)))
                TranslationProviders.Add(new($"agent:{id}", "agent", $"Agent · {id}"));
        foreach (var c in CustomTranslationProviders)
            TranslationProviders.Add(new(c.Id, "openai", string.IsNullOrWhiteSpace(c.Name) ? c.Endpoint : c.Name));
    }

    private void PullSettingsToEditor()
    {
        SettingsClaudePath = _claudePath;
        SettingsCodexPath = _codexPath;
        SettingsAgyPath = _agyPath;
        SettingsPiPath = _piPath;
        SettingsPiWorkerPath = _piWorkerPath;
        SettingsOllamaEndpoint = _ollamaEndpoint;
        SettingsAllowRemoteOllama = _settings.AllowRemoteOllamaEndpoint;
        SettingsOllamaModel = _ollamaModel;
        SettingsOllamaTimeoutSeconds = _settings.OllamaTimeoutSeconds;
        LoadCustomProvidersEditor();
        RebuildTranslationProviders();
        SettingsTranslationSelectedId = _settings.TranslationSelectedId;
        SettingsTranslationAgentModel = _settings.TranslationAgentModel;
        SettingsDefaultTranslationEnabled = TranslationEnabled;
        SettingsTranslateSource = _translateSource;
        SettingsTranslateTarget = _translateTarget;
        SettingsWarnNoWorktree = _warnNoWorktree;
        SettingsTheme = Theme.ThemePalette.Normalize(_theme);
        SettingsLanguage = _language == "en" ? "en" : "ko";
        SettingsApprovalPolicy = _approvalPolicy;
        SettingsWorktreeBase = _worktreeBase;
        SettingsAutoStart = _autoStartLastSession;
        SettingsStreamLogs = _settings.StreamLogs;
        SettingsSkillContent = _skillContent;
        SettingsSkillDirCc = _skillDirs.GetValueOrDefault("cc", "");
        SettingsSkillDirGx = _skillDirs.GetValueOrDefault("gx", "");
        SettingsSkillDirAgy = _skillDirs.GetValueOrDefault("agy", "");
        SettingsSkillDirPi = _skillDirs.GetValueOrDefault("pi", "");
        SettingsModelCc = DefaultModelFor("cc");
        SettingsModelGx = DefaultModelFor("gx");
        SettingsModelAgy = DefaultModelFor("agy");
        SettingsModelPi = DefaultModelFor("pi");
        SeedStaticChecklists();   // cc/gx/agy 체크리스트를 정적 모델로 채움(pi는 조회 후); 선호 체크 반영
        SettingsAccent = _accent;
        SettingsAccentCustom = Theme.AccentPalette.IsHex(_accent) ? _accent : "";
        SettingsTelemetry = _telemetry;
        SettingsEngineCc = !_disabledEngines.Contains("cc");
        SettingsEngineGx = !_disabledEngines.Contains("gx");
        SettingsEngineAgy = !_disabledEngines.Contains("agy");
        SettingsEnginePi = !_disabledEngines.Contains("pi");
        SettingsAuthCc = _engineAuth.GetAuthMode("cc");
        SettingsAuthGx = _engineAuth.GetAuthMode("gx");
        SettingsAuthAgy = _engineAuth.GetAuthMode("agy");
        SettingsAutoApiCc = _engineAuth.GetAutoApi("cc");
        SettingsAutoApiGx = _engineAuth.GetAutoApi("gx");
        SettingsAutoApiAgy = _engineAuth.GetAutoApi("agy");
        SettingsApiKeyCc = _engineAuth.GetApiKeyPlain("cc");
        SettingsApiKeyGx = _engineAuth.GetApiKeyPlain("gx");
        SettingsApiKeyAgy = _engineAuth.GetApiKeyPlain("agy");
        RefreshDetectLabels(); // CLI 경로 감지 라벨 + 로그인 계정 표시 갱신
    }

    private void OpenSettings()
    {
        PullSettingsToEditor();
        SettingsStatus = "";
        _ = RefreshOllamaStatusAsync();   // 설정 열 때 Ollama 상태 최신화(Ollama 설정 패널 pill)
        _ = RefreshTranslationStatusAsync(); // 선택된 번역 provider 준비 상태 최신화(토글/경고 어포던스)
        _ = QueryPiModelsAsync();         // pi 모델/연동 provider 조회(첫 호출만 캐시)
        _ = QueryAgyModelsAsync();        // agy 모델 목록 조회(`agy models`, 첫 호출만 캐시)
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
            Shell.Open(Persistence.SettingsStore.SettingsPath);
        }
        catch { }
    }

    /// <summary>Open the per-engine config folder (engines/*.json) in the file manager. This is the source for each
    /// engine's models/efforts/path/auth (it replaced models.json). Edits apply on next app start.</summary>
    private void OpenModelsFile()
    {
        try
        {
            var dir = AgentManager.Core.Engines.EngineConfigStore.DefaultDir;
            System.IO.Directory.CreateDirectory(dir);
            Shell.Open(dir);
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
        _piPath = Clean(SettingsPiPath);
        _piWorkerPath = Clean(SettingsPiWorkerPath);
        RefreshDetectLabels();
        _ollamaEndpoint = string.IsNullOrWhiteSpace(SettingsOllamaEndpoint) ? "http://localhost:11434" : SettingsOllamaEndpoint.Trim();
        _settings.AllowRemoteOllamaEndpoint = SettingsAllowRemoteOllama;
        _ollamaModel = string.IsNullOrWhiteSpace(SettingsOllamaModel) ? "exaone3.5:7.8b" : SettingsOllamaModel.Trim();
        _settings.OllamaTimeoutSeconds = Math.Clamp(SettingsOllamaTimeoutSeconds, 10, 600);
        TranslationEnabled = SettingsDefaultTranslationEnabled;
        _warnNoWorktree = SettingsWarnNoWorktree;
        _approvalPolicy = SettingsApprovalPolicy is "ask" or "safe" ? SettingsApprovalPolicy : "yolo";
        _worktreeBase = (SettingsWorktreeBase ?? "").Trim();
        _autoStartLastSession = SettingsAutoStart;
        StreamLogs = SettingsStreamLogs;
        SetDefaultModel("cc", SettingsModelCc);
        SetDefaultModel("gx", SettingsModelGx);
        SetDefaultModel("agy", SettingsModelAgy);
        SetDefaultModel("pi", SettingsModelPi);
        SyncPreferredToStore(); // "주로 쓰는 모델" 체크 상태를 engines/*.json에 반영(+ 커스텀 모델 추가)
        _accent = Theme.AccentPalette.Normalize(SettingsAccent);
        _telemetry = SettingsTelemetry;
        // 엔진 비활성 집합 재구성 — 단, 최소 1개는 활성으로 유지
        var disabled = new HashSet<string>();
        if (!SettingsEngineCc) disabled.Add("cc");
        if (!SettingsEngineGx) disabled.Add("gx");
        if (!SettingsEngineAgy) disabled.Add("agy");
        if (!SettingsEnginePi) disabled.Add("pi");
        if (disabled.Count < AllEngines.Length)
        {
            _disabledEngines.Clear();
            foreach (var d in disabled) _disabledEngines.Add(d);
            OnChanged(nameof(Engines));
        }
        SaveEngineAuth("cc", SettingsAuthCc, SettingsApiKeyCc);
        SaveEngineAuth("gx", SettingsAuthGx, SettingsApiKeyGx);
        SaveEngineAuth("agy", SettingsAuthAgy, SettingsApiKeyAgy);
        _engineAuth.SetAutoApi("cc", SettingsAutoApiCc);
        _engineAuth.SetAutoApi("gx", SettingsAutoApiGx);
        _engineAuth.SetAutoApi("agy", SettingsAutoApiAgy);
        SyncAuthToStore(); // 인증(모드/암호화 키/auto-api)을 engines/*.json에 반영
        _theme = Theme.ThemePalette.Normalize(SettingsTheme); // 테마는 이미 라이브 적용됨
        var newLanguage = SettingsLanguage == "en" ? "en" : "ko";
        var languageChanged = newLanguage != _language;
        _language = newLanguage;
        _translateSource = NormalizeTranslationLang(SettingsTranslateSource, "Korean");
        _translateTarget = NormalizeTranslationLang(SettingsTranslateTarget, "English");
        _settings.TranslationCustomProviders = CustomTranslationProviders.ToList();
        _settings.TranslationAgentModel = (SettingsTranslationAgentModel ?? "").Trim();
        _settings.TranslationSelectedId = string.IsNullOrWhiteSpace(SettingsTranslationSelectedId) ? "ollama" : SettingsTranslationSelectedId;
        _translator = BuildTranslator(); // selected provider (ollama / agent / custom) via the factory
        _ = RefreshTranslationStatusAsync(); // provider가 바뀌었을 수 있으니 준비 상태 재평가(토글/경고 게이트)
        ApplyAndInjectSkill(); // 각 엔진 스킬 폴더에 SKILL.md 기록
        SyncEngineFlagsToStore(); // enabled + skill-dir를 engines/*.json에 반영
        SettingsStatus = languageChanged ? L("L.SettingsSavedRestart") : L("L.SettingsSaved");
        SaveState();
    }

    private static string Clean(string value) => CoreHelpers.Clean(value);

    /// <summary>번역 언어 값을 선택지(영어 표기)로 정규화. 미상이면 기본값.</summary>
    private string NormalizeTranslationLang(string? value, string fallback)
    {
        return CoreHelpers.NormalizeTranslationLang(
            value,
            fallback,
            AvailableTranslationLanguages.Select(l => l.Id));
    }

}
