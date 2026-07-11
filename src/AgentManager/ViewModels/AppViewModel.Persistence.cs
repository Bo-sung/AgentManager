using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Usage;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    /// <summary>settings.json(또는 마이그레이션)에서 읽은 설정을 VM 필드에 적용하고 테마/강조색/번역기를
    /// 재구성한다. 시작 시(RestoreState)와 외부 편집 리로드(ReloadSettingsFromDisk) 양쪽에서 쓰인다.</summary>
    private void ApplySettings(AppSettingsDto s)
    {
        _claudePath = s.ClaudePath;
        _codexPath = s.CodexPath;
        _agyPath = s.AgyPath;
        _piPath = s.PiPath;
        _piWorkerPath = s.PiWorkerPath;
        foreach (var kv in s.PreferredModels ?? new())
            if (_preferred.TryGetValue(kv.Key, out var set)) { set.Clear(); foreach (var m in kv.Value) set.Add(m); }
        _ollamaEndpoint = string.IsNullOrWhiteSpace(s.OllamaEndpoint) ? _ollamaEndpoint : s.OllamaEndpoint;
        _ollamaModel = string.IsNullOrWhiteSpace(s.OllamaModel) ? _ollamaModel : s.OllamaModel;
        _settings.OllamaTimeoutSeconds = s.OllamaTimeoutSeconds is >= 10 and <= 600 ? s.OllamaTimeoutSeconds : 60;
        TranslationEnabled = s.TranslationEnabled;
        MaxConcurrentSessions = s.MaxConcurrentSessions;
        MaxConcurrentWorkers = s.MaxConcurrentWorkers < 1 ? AgentManager.Core.Workers.WorkerDefaults.DefaultMaxConcurrentWorkers : s.MaxConcurrentWorkers;
        WorkerBehaviorPreamble = string.IsNullOrWhiteSpace(s.WorkerBehaviorPreamble) ? AgentManager.Core.Workers.WorkerDefaults.BehaviorPreamble : s.WorkerBehaviorPreamble;
        _skillContent = string.IsNullOrWhiteSpace(s.SkillContent) ? AgentManager.Core.SkillInjector.WorkerPromptDefault : s.SkillContent;
        _skillDirs = MergeSkillDirs(s.SkillDirs);
        // 구조화 ask-user 스킬은 고정 콘텐츠 → 시작 시 자동 주입(설정 저장을 기다리지 않게). 멱등.
        try { AgentManager.Core.SkillInjector.Inject(AgentManager.Core.SkillInjector.AskUserDefault, _skillDirs); } catch { }
        _settings.ReviewPaneOpen = s.ReviewPaneOpen;
        _warnNoWorktree = s.WarnNoWorktree;
        _approvalPolicy = s.ApprovalPolicy is "ask" or "safe" ? s.ApprovalPolicy : "yolo";
        _worktreeBase = (s.WorktreeBase ?? "").Trim();
        _autoStartLastSession = s.AutoStartLastSession;
        _settings.StreamLogs = s.StreamLogs;
        _defaultModels = s.DefaultModels ?? new();
        _accent = Theme.AccentPalette.Normalize(s.Accent);
        // UI 줌: 본문/모달 독립 배율. 구버전(UiScale/ZoomScope) 마이그레이션.
        var legacy = s.UiScale > 0 ? s.UiScale : 1.0;
        _bodyScale = ClampZoom(s.BodyScale > 0 ? s.BodyScale : legacy);
        _modalScale = ClampZoom(s.ModalScale > 0 ? s.ModalScale : (s.ZoomScope == "body" ? 1.0 : legacy));
        _telemetry = s.Telemetry;
        _disabledEngines.Clear();
        foreach (var d in s.DisabledEngines ?? []) _disabledEngines.Add(d);
        _dismissedCliSessions.Clear();
        foreach (var d in s.DismissedCliSessions ?? []) _dismissedCliSessions.Add(d);
        _engineAuth.Load(s.EngineAuthMode, s.EngineApiKey, s.EngineAutoApiOnLimit, s.EngineLimitedUntil);
        _usageService.Clear();
        foreach (var kv in s.Usage ?? new())
            _usageService.Set(kv.Key, new UsageSnapshot(kv.Value.Utilization, kv.Value.ResetsAtUnix, kv.Value.RateLimitType ?? "", kv.Value.CapturedUtc));
        _theme = Theme.ThemePalette.Normalize(s.Theme);
        _language = s.Language == "en" ? "en" : "ko";
        _translateSource = NormalizeTranslationLang(s.TranslateSourceLanguage, "Korean");
        _translateTarget = NormalizeTranslationLang(s.TranslateTargetLanguage, "English");
        _translator = BuildTranslator();
        Theme.ThemePalette.Apply(_theme);
        Theme.AccentPalette.Apply(_accent);
    }

    /// <summary>현재 설정을 settings.json DTO로 직렬화.</summary>
    private AppSettingsDto BuildSettingsDto() => new()
    {
        ClaudePath = _claudePath,
        CodexPath = _codexPath,
        AgyPath = _agyPath,
        PiPath = _piPath,
        PiWorkerPath = _piWorkerPath,
        PreferredModels = _preferred.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
        OllamaEndpoint = _ollamaEndpoint,
        OllamaModel = _ollamaModel,
        OllamaTimeoutSeconds = _settings.OllamaTimeoutSeconds,
        TranslationEnabled = TranslationEnabled,
        MaxConcurrentSessions = MaxConcurrentSessions,
        MaxConcurrentWorkers = MaxConcurrentWorkers,
        WorkerBehaviorPreamble = WorkerBehaviorPreamble,
        SkillContent = _skillContent,
        SkillDirs = new Dictionary<string, string>(_skillDirs),
        ReviewPaneOpen = IsReviewOpen,
        WarnNoWorktree = _warnNoWorktree,
        Theme = _theme,
        Language = _language,
        TranslateSourceLanguage = _translateSource,
        TranslateTargetLanguage = _translateTarget,
        ApprovalPolicy = _approvalPolicy,
        WorktreeBase = _worktreeBase,
        AutoStartLastSession = _autoStartLastSession,
        StreamLogs = _settings.StreamLogs,
        DefaultModels = new Dictionary<string, string>(_defaultModels),
        Accent = _accent,
        BodyScale = _bodyScale,
        ModalScale = _modalScale,
        Telemetry = _telemetry,
        DisabledEngines = _disabledEngines.ToList(),
        DismissedCliSessions = _dismissedCliSessions.ToList(),
        EngineAuthMode = _engineAuth.SnapshotAuthMode(),
        EngineApiKey = _engineAuth.SnapshotApiKey(),
        EngineAutoApiOnLimit = _engineAuth.SnapshotAutoApi(),
        EngineLimitedUntil = _engineAuth.SnapshotLimitedUntil(),
        Usage = _usageService.Snapshots.ToDictionary(k => k.Key, v => new UsageSnapshotDto
        {
            Utilization = v.Value.Utilization,
            ResetsAtUnix = v.Value.ResetsAtUnix,
            RateLimitType = v.Value.RateLimitType,
            CapturedUtc = v.Value.CapturedUtc,
        }),
    };

    private void RestoreState()
    {
        ApplySettings(SettingsStore.Load());
        var state = AppStateStore.Load();
        if (state is null || state.Projects.Count == 0)
        {
            var repo = FindRepoRoot();
            var project = new ProjectViewModel(Slug("workspace") + "-" + DateTime.Now.Ticks, "workspace", repo);
            Projects.Add(project);
            ActiveProject = project;
            return;
        }

        foreach (var p in state.Projects.Where(p => Directory.Exists(p.Path)))
        {
            var pvm = new ProjectViewModel(p.Id, p.Name, p.Path) { McpConfigPath = p.McpConfigPath };
            foreach (var extra in p.ExtraPaths.Where(Directory.Exists))
                pvm.ExtraPaths.Add(extra);
            Projects.Add(pvm);
        }

        if (Projects.Count == 0)
        {
            var repo = FindRepoRoot();
            Projects.Add(new ProjectViewModel(Slug("workspace") + "-" + DateTime.Now.Ticks, "workspace", repo));
        }

        // 세션 + 워커 백로그는 프로젝트 로컬(<project>/.am/project.json)에서 읽는다.
        // 로컬 파일이 없는 프로젝트는 레거시 전역 state.json의 슬라이스로 폴백 = 1회 마이그레이션.
        // (전역 파일의 Sessions/WorkerTasks는 더 이상 기록되지 않으므로, 첫 저장 후엔 로컬만 남는다.)
        var legacySessions = state.Sessions ?? [];
        var legacyTasks = state.WorkerTasks ?? [];
        var restoredTasks = new List<AgentManager.Core.Workers.WorkerTaskDto>();
        var migrationPending = false; // 레거시 전역 데이터가 프로젝트 로컬로 아직 옮겨지지 않았는지(1회 마이그레이션)
        foreach (var project in Projects)
        {
            var local = ProjectStateStore.Load(project.Path);
            List<SessionDto> sessionsForProject;
            List<AgentManager.Core.Workers.WorkerTaskDto> tasksForProject;
            if (local is not null)
            {
                sessionsForProject = local.Sessions;
                tasksForProject = local.WorkerTasks;
            }
            else
            {
                // 마이그레이션: 레거시 전역 배열에서 이 프로젝트 소속 슬라이스를 가져온다.
                // 실제로 옮길 데이터가 있으면 마이그레이션 저장을 예약한다(전역 중복 제거).
                sessionsForProject = legacySessions.Where(s => s.ProjectId == project.Id).ToList();
                tasksForProject = legacyTasks.Where(t => t.ProjectId == project.Id).ToList();
                if (sessionsForProject.Count > 0 || tasksForProject.Count > 0) migrationPending = true;
            }
            restoredTasks.AddRange(tasksForProject);
            foreach (var dto in sessionsForProject)
            {
                var engine = EngineRegistry.Get(dto.AgentId);
                // 모델 카탈로그가 바뀌면(예: codex gpt-5.1 계열 폐기) 저장된 구 모델 id를 현행 기본값으로 정규화
                var model = engine.Models.Contains(dto.Model) ? dto.Model : engine.Models[0];
                var s = new SessionViewModel(
                    dto.Id,
                    engine,
                    dto.Title,
                    dto.Branch,
                    dto.ProjectId,
                    dto.Project,
                    dto.ProjectPath,
                    model,
                    dto.StartedAt)
                {
                    WorktreePath = Directory.Exists(dto.WorktreePath) ? dto.WorktreePath : null,
                    Isolated = dto.Isolated && Directory.Exists(dto.WorktreePath),
                    WorktreeAttempted = dto.WorktreeAttempted,
                    WorktreeOptOut = dto.WorktreeOptOut,
                    Status = dto.Status is "running" or "waiting" ? "idle" : dto.Status,
                    Activity = dto.Status is "running" or "waiting" ? L("L.RestoredAfterRestart") : dto.Activity,
                    TokensIn = dto.TokensIn,
                    TokensOut = dto.TokensOut,
                    CostUsd = dto.CostUsd,
                    IsArchived = dto.IsArchived,
                    Sandbox = Enum.TryParse<SandboxMode>(dto.Sandbox, out var sb) ? sb : SandboxMode.DangerFullAccess,
                    RequireApproval = dto.RequireApproval,
                    ReasoningEffort = !string.IsNullOrWhiteSpace(dto.ReasoningEffort) ? dto.ReasoningEffort
                        : dto.AgentId == "gx" ? "medium" : "default",
                    TranslationEnabled = dto.TranslationEnabled ?? true,
                    EngineSessionId = dto.EngineSessionId,
                    Role = Enum.TryParse<AgentManager.Core.Workers.SessionRole>(dto.Role, out var role) ? role : AgentManager.Core.Workers.SessionRole.Plain,
                    BehaviorPreamble = dto.BehaviorPreamble ?? "",
                    TranslateSourceLanguage = dto.TranslateSourceLanguage,
                    TranslateTargetLanguage = dto.TranslateTargetLanguage,
                    LastMainSessionId = dto.LastMainSessionId,
                };
                foreach (var item in dto.Transcript)
                    s.Transcript.Add(AppStateStore.FromDto(item));
                foreach (var a in dto.Artifacts)
                    s.Artifacts.Add(new ArtifactViewModel(a.Kind, a.Title) { Content = a.Content, IsError = a.IsError });
                s.PropertyChanged += SessionStatusWatch;
                _allSessions.Add(s);
            }
        }
        LoadWorkerTasks(restoredTasks);
        ActiveProject = Projects.FirstOrDefault(p => p.Id == state.ActiveProjectId) ?? Projects[0];
        RefreshProjectSessions();
        RefreshQuotaText(); // 복원된 사용량 스냅샷을 footer에 즉시 표시(신선도 라벨 포함)
        // 1회 마이그레이션: 레거시 전역 데이터를 프로젝트 로컬로 옮기고 전역 중복을 제거한다.
        // SaveState는 디바운스되므로 생성자 중에 호출해도 안전(수백 ms 후 한 번 기록).
        if (migrationPending) SaveState();
    }

    // ----- 영속화: 디바운스(코얼레싱) + 오프-UI 쓰기 + 종료 시 강제 flush -----
    // 모든 변경마다 전체 상태를 동기 직렬화/쓰기하던 것을, 한 턴의 연속 변경을 한 번의 쓰기로 합치도록 바꾼다.
    // DTO 빌드는 반드시 UI 스레드(VM 컬렉션은 UI-affine)에서 하고, 완성된 DTO만 백그라운드 Task로 넘겨 파일에 쓴다.
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SaveMaxLatency = TimeSpan.FromSeconds(1); // 활동이 있어도 ~1s 내에는 반드시 저장

    private static readonly string PersistErrorLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "save-errors.log");

    /// <summary>영속화 실패를 save-errors.log에 추가 기록(진단용). 절대 throw 안 함 — 저장 실패가 UI/런을 막아선 안 된다.</summary>
    private static void LogPersistError(string source, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PersistErrorLogPath)!);
            File.AppendAllText(PersistErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch { }
    }

    // 영속 '메커니즘'(디바운스 타이머·빌드(UI)/쓰기(백그라운드) 분리·결과 핸드오프)은 Core ProjectStore가 소유한다.
    // VM은 라이브 OC를 읽어 DTO를 만드는 build와, 그 불변 스냅샷을 디스크에 쓰는 write만 제공(둘 다 WPF/VM 측). overhaul (a) step 3.
    private sealed record SaveSnapshot(AppSettingsDto Settings, AppStateDto State, List<(string Path, ProjectStateDto Dto)> ProjectStates);
    private AgentManager.Core.Persistence.ProjectStore<SaveSnapshot>? _projectStoreBacking;
    private AgentManager.Core.Persistence.ProjectStore<SaveSnapshot> _projectStore => _projectStoreBacking ??= new(
        build: () => new SaveSnapshot(BuildSettingsDto(), BuildStateDto(), BuildProjectStates()),
        write: s => WriteSnapshot(s.Settings, s.State, s.ProjectStates),
        postToOwner: PostToOwner,
        report: ReportSaveResult,
        logError: LogPersistError,
        canDebounce: () => Application.Current?.Dispatcher is not null,
        debounce: SaveDebounce, maxLatency: SaveMaxLatency);

    /// <summary>build(UI 스레드)→write(백그라운드) 핸드오프와 저장 결과 보고를 UI 스레드로 마샬. 디스패처 없으면 인라인.</summary>
    private static void PostToOwner(Action a)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp is not null) disp.InvokeAsync(a);
        else a();
    }

    /// <summary>코얼레싱 저장 예약 — 한 턴의 연속 변경을 한 번의 쓰기로 합친다(디바운스 + 최대 지연 캡은 ProjectStore가 담당).</summary>
    private void SaveState() => _projectStore.Save();

    /// <summary>지금 즉시 저장 — 반드시-잃으면-안-되는 경로(앱 종료 등)에서 호출. 대기 중 디바운스도 취소한다.
    /// DTO는 UI 스레드에서 빌드하고(컬렉션 접근), 종료 경로에선 백그라운드 Task가 완료 전에 죽을 수 있으므로
    /// 동기로 파일까지 쓴다.</summary>
    internal void FlushStateNow(bool synchronousWrite = false) => _projectStore.Flush(synchronousWrite);

    /// <summary>완성된 DTO 스냅샷을 디스크에 쓴다. settings/state를 독립적으로 try해 한쪽 실패가 다른 쪽을 막지 않게 한다.
    /// WriteAtomic이 일시적 잠금을 재시도하고도 실패하면 그 예외가 여기서 잡혀 save-errors.log에 남는다.</summary>
    /// <summary>Returns whether the GLOBAL state write succeeded (settings/project-local failures are logged
    /// but don't alarm — a temporarily-unreachable project folder shouldn't trip the save banner).</summary>
    private static bool WriteSnapshot(AppSettingsDto settings, AppStateDto state, List<(string Path, ProjectStateDto Dto)> projectStates)
    {
        try { SettingsStore.Save(settings); }
        catch (Exception ex) { LogPersistError("settings.json", ex); }
        // 프로젝트 로컬 파일 먼저(세션·백로그). 읽기 전용/공유 드라이브 실패는 로그만 남기고 흡수.
        foreach (var (projectPath, dto) in projectStates)
        {
            try { ProjectStateStore.Save(projectPath, dto); }
            catch (Exception ex) { LogPersistError($"project.json {projectPath}", ex); }
        }
        try { AppStateStore.Save(state); return true; }
        catch (Exception ex) { LogPersistError("state.json", ex); return false; }
    }

    private int _consecutiveSaveFailures;
    private bool _saveFailing;
    /// <summary>Two+ consecutive state-save failures (e.g. a held file lock) → a non-blocking banner warns
    /// the user that changes may not be persisted. Cleared on the next successful save (P4). The actual
    /// exceptions are in save-errors.log.</summary>
    public bool SaveFailing { get => _saveFailing; private set => Set(ref _saveFailing, value); }

    private void ReportSaveResult(bool ok)
    {
        if (ok) { _consecutiveSaveFailures = 0; SaveFailing = false; }
        else if (++_consecutiveSaveFailures >= 2) SaveFailing = true;
    }

    /// <summary>현재 앱 상태를 state.json DTO로 직렬화(UI 스레드에서만 호출).
    /// 세션·워커태스크는 프로젝트 로컬(<see cref="BuildProjectStates"/>)로 분리 — 전역엔 프로젝트 목록/활성 id만 남는다.
    /// (레거시 호환을 위해 필드는 유지하되 항상 빈 배열로 기록 → 마이그레이션 후엔 중복이 사라진다.)</summary>
    private AppStateDto BuildStateDto() => new()
    {
        ActiveProjectId = ActiveProject?.Id,
        Projects = Projects.Select(p => new ProjectDto { Id = p.Id, Name = p.Name, Path = p.Path, McpConfigPath = p.McpConfigPath, ExtraPaths = p.ExtraPaths.ToList() }).ToList(),
        WorkerTasks = [],
        Sessions = [],
    };

    /// <summary>프로젝트 로컬 상태 스냅샷 — 각 프로젝트의 세션 + 워커 백로그/큐를 projectId로 그룹핑해
    /// <c>&lt;project.Path&gt;/.am/project.json</c> 용 DTO 리스트로 만든다(UI 스레드에서만 호출).</summary>
    private List<(string Path, ProjectStateDto Dto)> BuildProjectStates()
    {
        var result = new List<(string, ProjectStateDto)>();
        var tasksByProject = _taskStore.Snapshot()
            .Where(t => !string.IsNullOrEmpty(t.ProjectId))
            .GroupBy(t => t.ProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AgentManager.Core.Workers.WorkerTaskDto>)g.ToList());
        foreach (var p in Projects)
        {
            if (string.IsNullOrWhiteSpace(p.Path)) continue;
            var sessions = _allSessions.Where(s => s.ProjectId == p.Id).Select(BuildSessionDto).ToList();
            tasksByProject.TryGetValue(p.Id, out var tasks);
            result.Add((p.Path, new ProjectStateDto { Sessions = sessions, WorkerTasks = (tasks ?? []).ToList() }));
        }
        return result;
    }

    /// <summary>한 세션을 SessionDto로 매핑. 매핑 중 예외가 나면(예: 손상된 transcript 블록) 식별 필드만 담은
    /// 최소 DTO로 대체하고 기록한다 — 한 세션의 실패가 전체 저장을 막지 않도록(per-session fault isolation).</summary>
    private static SessionDto BuildSessionDto(SessionViewModel s)
    {
        try
        {
            return new SessionDto
            {
                Id = s.Id,
                AgentId = s.AgentId,
                Title = s.Title,
                Branch = s.Branch,
                ProjectId = s.ProjectId,
                Project = s.Project,
                ProjectPath = s.ProjectPath,
                Model = s.Model,
                TranslationEnabled = s.TranslationEnabled,
                EngineSessionId = s.EngineSessionId,
                Role = s.Role.ToString(),
                BehaviorPreamble = s.BehaviorPreamble,
                TranslateSourceLanguage = s.TranslateSourceLanguage,
                TranslateTargetLanguage = s.TranslateTargetLanguage,
                LastMainSessionId = s.LastMainSessionId,
                Status = s.Status,
                Activity = s.Activity,
                TokensIn = s.TokensIn,
                TokensOut = s.TokensOut,
                CostUsd = s.CostUsd,
                IsArchived = s.IsArchived,
                Sandbox = s.Sandbox.ToString(),
                RequireApproval = s.RequireApproval,
                ReasoningEffort = s.ReasoningEffort,
                Artifacts = s.Artifacts.Select(a => new ArtifactDto { Kind = a.Kind, Title = a.Title, Content = a.Content, IsError = a.IsError }).ToList(),
                StartedAt = s.StartedAt,
                WorktreePath = s.WorktreePath,
                Isolated = s.Isolated,
                WorktreeAttempted = s.WorktreeAttempted,
                WorktreeOptOut = s.WorktreeOptOut,
                Transcript = s.Transcript.Select(AppStateStore.ToDto).ToList(),
            };
        }
        catch (Exception ex)
        {
            // 식별/필수 필드만 보존(빈 transcript) — 이 세션의 본문은 손실되지만 나머지 세션은 정상 저장된다.
            LogPersistError($"session {s.Id}", ex);
            return new SessionDto
            {
                Id = s.Id,
                AgentId = s.AgentId,
                Title = s.Title,
                ProjectId = s.ProjectId,
                Branch = s.Branch,
                Project = s.Project,
                ProjectPath = s.ProjectPath,
                Model = s.Model,
                Status = s.Status,
            };
        }
    }

    // ----- settings.json 외부 편집 감시 (VS Code식 라이브 리로드) -----
    private FileSystemWatcher? _settingsWatcher;

    /// <summary>settings.json 파일 변경을 감시해 외부(손)편집을 라이브 반영한다.</summary>
    private void StartSettingsWatcher()
    {
        try
        {
            var path = SettingsStore.SettingsPath;
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            _settingsWatcher = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            void OnChange(object _, FileSystemEventArgs __) => _ = DebouncedReloadAsync();
            _settingsWatcher.Changed += OnChange;
            _settingsWatcher.Created += OnChange;
            _settingsWatcher.Renamed += (_, _) => _ = DebouncedReloadAsync();
            // 버퍼 오버플로 등으로 감시가 죽으면 조용히 멈춘다 → 재무장(수동 새로고침 버튼이 최종 안전장치).
            _settingsWatcher.Error += (_, _) =>
            {
                try { if (_settingsWatcher is { } w) { w.EnableRaisingEvents = false; w.EnableRaisingEvents = true; } }
                catch { }
            };
        }
        catch { }
    }

    private async Task DebouncedReloadAsync()
    {
        await Task.Delay(250).ConfigureAwait(false); // 편집기가 여러 이벤트를 쏘므로 디바운스
        var disp = Application.Current?.Dispatcher;
        if (disp is not null) await disp.InvokeAsync(ReloadSettingsFromDisk);
    }

    /// <summary>파일 감시(디바운스) 경로. 조용히 적용(변경 없음/자기-쓰기는 무시). 파싱 실패 시 상태만 표시.</summary>
    private void ReloadSettingsFromDisk() => ApplyDiskSettings(manual: false);

    /// <summary>수동 "설정 새로고침" 버튼 경로. 결과(적용/변경 없음/파싱 실패)를 상태 라벨로 보고.</summary>
    private void ReloadSettingsFile() => ApplyDiskSettings(manual: true);

    /// <summary>디스크의 settings.json을 읽어 VM에 적용. 자기-쓰기는 내용 비교로 무시(루프 방지).
    /// <b>읽기/파싱 실패 시 적용하지 않는다</b> — 일시적 문법 오류·잠금을 기본값으로 오인해 덮어써 설정이
    /// 통째로 날아가는 것을 막는다(<see cref="SettingsStore.TryLoad"/>가 실패를 null로 신호).</summary>
    private void ApplyDiskSettings(bool manual)
    {
        AppSettingsDto? disk;
        try { disk = SettingsStore.TryLoad(); } catch { disk = null; }
        if (disk is null)
        {
            SettingsStatus = AgentManager.App.L("L.SettingsReloadFailed");
            return; // 파싱/읽기 실패 → 절대 적용 안 함(설정 보존)
        }
        var opts = new JsonSerializerOptions();
        if (JsonSerializer.Serialize(disk, opts) == JsonSerializer.Serialize(BuildSettingsDto(), opts))
        {
            if (manual) SettingsStatus = AgentManager.App.L("L.SettingsReloadNoChange");
            return; // 우리가 쓴 내용과 동일 → 외부 변경 아님
        }
        ApplySettings(disk);
        // 설정 화면을 열지 않은 상태에서도 파생 바인딩(엔진 목록·배율·정책 등)이 즉시 반영되도록 전체 리프레시.
        // WPF에서 null/"" 프로퍼티명은 "모든 속성 변경"을 의미해 이 VM에 걸린 모든 바인딩을 재평가시킨다.
        // (언어 전환만은 리소스 사전 교체가 필요해 재시작 시 반영 — 저장 경로와 동일한 정책.)
        OnChanged(string.Empty);
        if (CurrentView == MainViewKind.Settings) PullSettingsToEditor();
        if (manual) SettingsStatus = AgentManager.App.L("L.SettingsReloaded");
    }

    private static readonly string DefaultWorktreesRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "worktrees");
    /// <summary>설정된 worktree 기준 경로 (빈 값이면 기본 앱 데이터 폴더).</summary>
    private string WorktreesRoot => string.IsNullOrWhiteSpace(_worktreeBase) ? DefaultWorktreesRoot : _worktreeBase.Trim();

    /// <summary>Create the session's worktree once (branched from project HEAD). No-op if not a git repo.</summary>
}
