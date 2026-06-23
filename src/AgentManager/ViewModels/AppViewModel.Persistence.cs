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
        _ollamaEndpoint = string.IsNullOrWhiteSpace(s.OllamaEndpoint) ? _ollamaEndpoint : s.OllamaEndpoint;
        _ollamaModel = string.IsNullOrWhiteSpace(s.OllamaModel) ? _ollamaModel : s.OllamaModel;
        TranslationEnabled = s.TranslationEnabled;
        MaxConcurrentSessions = s.MaxConcurrentSessions;
        MaxConcurrentWorkers = s.MaxConcurrentWorkers < 1 ? AgentManager.Core.Workers.WorkerDefaults.DefaultMaxConcurrentWorkers : s.MaxConcurrentWorkers;
        WorkerBehaviorPreamble = string.IsNullOrWhiteSpace(s.WorkerBehaviorPreamble) ? AgentManager.Core.Workers.WorkerDefaults.BehaviorPreamble : s.WorkerBehaviorPreamble;
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
        _engineAuthMode.Clear();
        foreach (var kv in s.EngineAuthMode ?? new()) _engineAuthMode[kv.Key] = kv.Value;
        _engineApiKey.Clear();
        foreach (var kv in s.EngineApiKey ?? new()) _engineApiKey[kv.Key] = kv.Value;
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
        OllamaEndpoint = _ollamaEndpoint,
        OllamaModel = _ollamaModel,
        TranslationEnabled = TranslationEnabled,
        MaxConcurrentSessions = MaxConcurrentSessions,
        MaxConcurrentWorkers = MaxConcurrentWorkers,
        WorkerBehaviorPreamble = WorkerBehaviorPreamble,
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
        EngineAuthMode = new Dictionary<string, string>(_engineAuthMode),
        EngineApiKey = new Dictionary<string, string>(_engineApiKey),
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

        ActiveProject = Projects.FirstOrDefault(p => p.Id == state.ActiveProjectId) ?? Projects[0];
        RefreshProjectSessions();
        RefreshQuotaText(); // 복원된 사용량 스냅샷을 footer에 즉시 표시(신선도 라벨 포함)
    }

    private void SaveState()
    {
        try
        {
            // 설정은 settings.json으로 분리 저장(SettingsStore). 자기-쓰기로 인한 리로드는 ReloadSettingsFromDisk가 내용 비교로 무시.
            SettingsStore.Save(BuildSettingsDto());
            AppStateStore.Save(new AppStateDto
            {
                ActiveProjectId = ActiveProject?.Id,
                Projects = Projects.Select(p => new ProjectDto { Id = p.Id, Name = p.Name, Path = p.Path, McpConfigPath = p.McpConfigPath, ExtraPaths = p.ExtraPaths.ToList() }).ToList(),
                Sessions = _allSessions.Select(s => new SessionDto
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
                    Transcript = s.Transcript.Select(AppStateStore.ToDto).ToList(),
                }).ToList(),
            });
        }
        catch
        {
            // Persistence should never interrupt an agent run or UI interaction.
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
