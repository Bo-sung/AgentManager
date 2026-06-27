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
        foreach (var kv in s.PreferredModels ?? new())
            if (_preferred.TryGetValue(kv.Key, out var set)) { set.Clear(); foreach (var m in kv.Value) set.Add(m); }
        _ollamaEndpoint = string.IsNullOrWhiteSpace(s.OllamaEndpoint) ? _ollamaEndpoint : s.OllamaEndpoint;
        _ollamaModel = string.IsNullOrWhiteSpace(s.OllamaModel) ? _ollamaModel : s.OllamaModel;
        TranslationEnabled = s.TranslationEnabled;
        MaxConcurrentSessions = s.MaxConcurrentSessions;
        MaxConcurrentWorkers = s.MaxConcurrentWorkers < 1 ? AgentManager.Core.Workers.WorkerDefaults.DefaultMaxConcurrentWorkers : s.MaxConcurrentWorkers;
        WorkerBehaviorPreamble = string.IsNullOrWhiteSpace(s.WorkerBehaviorPreamble) ? AgentManager.Core.Workers.WorkerDefaults.BehaviorPreamble : s.WorkerBehaviorPreamble;
        _skillContent = string.IsNullOrWhiteSpace(s.SkillContent) ? AgentManager.Core.SkillInjector.WorkerPromptDefault : s.SkillContent;
        _skillDirs = MergeSkillDirs(s.SkillDirs);
        _isReviewOpen = s.ReviewPaneOpen;
        _warnNoWorktree = s.WarnNoWorktree;
        _approvalPolicy = s.ApprovalPolicy is "ask" or "safe" ? s.ApprovalPolicy : "yolo";
        _worktreeBase = (s.WorktreeBase ?? "").Trim();
        _autoStartLastSession = s.AutoStartLastSession;
        _streamLogs = s.StreamLogs;
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
        _engineAuthMode.Clear();
        foreach (var kv in s.EngineAuthMode ?? new()) _engineAuthMode[kv.Key] = kv.Value;
        _engineApiKey.Clear();
        foreach (var kv in s.EngineApiKey ?? new()) _engineApiKey[kv.Key] = kv.Value;
        _engineAutoApi.Clear();
        foreach (var kv in s.EngineAutoApiOnLimit ?? new()) _engineAutoApi[kv.Key] = kv.Value;
        _engineLimitedUntil.Clear();
        foreach (var kv in s.EngineLimitedUntil ?? new()) _engineLimitedUntil[kv.Key] = kv.Value;
        _usage.Clear();
        foreach (var kv in s.Usage ?? new())
            _usage[kv.Key] = new UsageSnapshot(kv.Value.Utilization, kv.Value.ResetsAtUnix, kv.Value.RateLimitType ?? "", kv.Value.CapturedUtc);
        _theme = Theme.ThemePalette.Normalize(s.Theme);
        _language = s.Language == "en" ? "en" : "ko";
        _translateSource = NormalizeTranslationLang(s.TranslateSourceLanguage, "Korean");
        _translateTarget = NormalizeTranslationLang(s.TranslateTargetLanguage, "English");
        _translator = CreateTranslator(_ollamaEndpoint, _ollamaModel, _translateSource, _translateTarget);
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
        PreferredModels = _preferred.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()),
        OllamaEndpoint = _ollamaEndpoint,
        OllamaModel = _ollamaModel,
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
        StreamLogs = _streamLogs,
        DefaultModels = new Dictionary<string, string>(_defaultModels),
        Accent = _accent,
        BodyScale = _bodyScale,
        ModalScale = _modalScale,
        Telemetry = _telemetry,
        DisabledEngines = _disabledEngines.ToList(),
        DismissedCliSessions = _dismissedCliSessions.ToList(),
        EngineAuthMode = new Dictionary<string, string>(_engineAuthMode),
        EngineApiKey = new Dictionary<string, string>(_engineApiKey),
        EngineAutoApiOnLimit = new Dictionary<string, bool>(_engineAutoApi),
        EngineLimitedUntil = new Dictionary<string, long>(_engineLimitedUntil),
        Usage = _usage.ToDictionary(k => k.Key, v => new UsageSnapshotDto
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

        foreach (var dto in state.Sessions)
        {
            var project = Projects.FirstOrDefault(p => p.Id == dto.ProjectId);
            if (project is null) continue;
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

        LoadWorkerTasks(state.WorkerTasks ?? []);
        ActiveProject = Projects.FirstOrDefault(p => p.Id == state.ActiveProjectId) ?? Projects[0];
        RefreshProjectSessions();
        RefreshQuotaText(); // 복원된 사용량 스냅샷을 footer에 즉시 표시(신선도 라벨 포함)
    }

    // ----- 영속화: 디바운스(코얼레싱) + 오프-UI 쓰기 + 종료 시 강제 flush -----
    // 모든 변경마다 전체 상태를 동기 직렬화/쓰기하던 것을, 한 턴의 연속 변경을 한 번의 쓰기로 합치도록 바꾼다.
    // DTO 빌드는 반드시 UI 스레드(VM 컬렉션은 UI-affine)에서 하고, 완성된 DTO만 백그라운드 Task로 넘겨 파일에 쓴다.
    private DispatcherTimer? _saveDebounce;
    private DateTime _firstPendingSaveUtc; // 디바운스가 무한정 밀리지 않도록 첫 변경 시각을 캡처(최대 지연 캡)
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

    /// <summary>코얼레싱 저장 예약 — 한 턴의 연속 변경을 한 번의 쓰기로 합친다.
    /// 디바운스 타이머를 매 변경마다 재시작하되, 첫 변경으로부터 SaveMaxLatency가 지나면 즉시 발화시켜
    /// 변경이 계속돼도 저장이 무한정 밀리지 않게 한다.</summary>
    private void SaveState()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp is null) { FlushStateNow(); return; } // 디스패처 없음(테스트/종료 중) → 동기 저장

        if (_saveDebounce is null)
        {
            _saveDebounce = new DispatcherTimer(DispatcherPriority.Background, disp) { Interval = SaveDebounce };
            _saveDebounce.Tick += (_, _) => { _saveDebounce!.Stop(); FlushStateNow(); };
        }

        if (!_saveDebounce.IsEnabled) _firstPendingSaveUtc = DateTime.UtcNow;
        // 최대 지연 캡 도달 시 더 미루지 않고 바로 저장
        if (DateTime.UtcNow - _firstPendingSaveUtc >= SaveMaxLatency)
        {
            _saveDebounce.Stop();
            FlushStateNow();
            return;
        }
        _saveDebounce.Stop();
        _saveDebounce.Start(); // 재시작으로 디바운스
    }

    /// <summary>지금 즉시 저장 — 반드시-잃으면-안-되는 경로(앱 종료 등)에서 호출. 대기 중 디바운스도 취소한다.
    /// DTO는 UI 스레드에서 빌드하고(컬렉션 접근), 종료 경로에선 백그라운드 Task가 완료 전에 죽을 수 있으므로
    /// 동기로 파일까지 쓴다.</summary>
    internal void FlushStateNow(bool synchronousWrite = false)
    {
        _saveDebounce?.Stop();
        try
        {
            // 설정은 settings.json으로 분리 저장(SettingsStore). 자기-쓰기로 인한 리로드는 ReloadSettingsFromDisk가 내용 비교로 무시.
            var settings = BuildSettingsDto();
            var state = BuildStateDto();
            // 완성된 DTO(불변 스냅샷)는 VM 컬렉션과 무관하므로 어느 스레드에서 써도 안전하다.
            // 종료 경로(synchronousWrite)에선 프로세스가 백그라운드 Task 완료 전에 죽을 수 있으니 동기로 끝까지 쓴다.
            if (synchronousWrite)
                ReportSaveResult(WriteSnapshot(settings, state));
            else
            {
                var disp = Application.Current?.Dispatcher;
                _ = Task.Run(() =>
                {
                    var ok = WriteSnapshot(settings, state);
                    // SaveFailing is bound to the UI — flip it back on the UI thread.
                    if (disp is not null) disp.InvokeAsync(() => ReportSaveResult(ok));
                    else ReportSaveResult(ok);
                });
            }
        }
        catch (Exception ex)
        {
            // DTO 빌드 자체의 예외(드묾) — 기록만 하고 흡수. 저장 실패가 에이전트 런/UI를 끊어선 안 된다.
            LogPersistError("BuildStateDto", ex);
            ReportSaveResult(false);
        }
    }

    /// <summary>완성된 DTO 스냅샷을 디스크에 쓴다. settings/state를 독립적으로 try해 한쪽 실패가 다른 쪽을 막지 않게 한다.
    /// WriteAtomic이 일시적 잠금을 재시도하고도 실패하면 그 예외가 여기서 잡혀 save-errors.log에 남는다.</summary>
    /// <summary>Returns whether the state write succeeded (settings failure alone doesn't alarm the user).</summary>
    private static bool WriteSnapshot(AppSettingsDto settings, AppStateDto state)
    {
        try { SettingsStore.Save(settings); }
        catch (Exception ex) { LogPersistError("settings.json", ex); }
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
    /// 세션별 매핑(특히 Transcript)을 각각 try로 격리해, 한 세션이 깨져도 나머지 세션 저장을 막지 않는다(P3).</summary>
    private AppStateDto BuildStateDto() => new()
    {
        ActiveProjectId = ActiveProject?.Id,
        Projects = Projects.Select(p => new ProjectDto { Id = p.Id, Name = p.Name, Path = p.Path, McpConfigPath = p.McpConfigPath, ExtraPaths = p.ExtraPaths.ToList() }).ToList(),
        WorkerTasks = WorkerTasksSnapshot().ToList(),
        Sessions = _allSessions.Select(BuildSessionDto).ToList(),
    };

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
        }
        catch { }
    }

    private async Task DebouncedReloadAsync()
    {
        await Task.Delay(250).ConfigureAwait(false); // 편집기가 여러 이벤트를 쏘므로 디바운스
        var disp = Application.Current?.Dispatcher;
        if (disp is not null) await disp.InvokeAsync(ReloadSettingsFromDisk);
    }

    /// <summary>디스크의 settings.json을 읽어 VM에 적용. 자기-쓰기는 내용 비교로 무시(루프 방지).</summary>
    private void ReloadSettingsFromDisk()
    {
        try
        {
            var disk = SettingsStore.Load();
            var opts = new JsonSerializerOptions();
            if (JsonSerializer.Serialize(disk, opts) == JsonSerializer.Serialize(BuildSettingsDto(), opts))
                return; // 우리가 쓴 내용과 동일 → 외부 변경 아님
            ApplySettings(disk);
            OnChanged(nameof(BodyScale));
            OnChanged(nameof(ModalScale));
            OnChanged(nameof(BodyScalePercent));
            OnChanged(nameof(ModalScalePercent));
            if (CurrentView == MainViewKind.Settings) PullSettingsToEditor();
        }
        catch { }
    }

    private static readonly string DefaultWorktreesRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "worktrees");
    /// <summary>설정된 worktree 기준 경로 (빈 값이면 기본 앱 데이터 폴더).</summary>
    private string WorktreesRoot => string.IsNullOrWhiteSpace(_worktreeBase) ? DefaultWorktreesRoot : _worktreeBase.Trim();

    /// <summary>Create the session's worktree once (branched from project HEAD). No-op if not a git repo.</summary>
}
