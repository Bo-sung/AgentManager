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

public sealed partial class AppViewModel
{
    private void RestoreState()
    {
        var state = AppStateStore.Load();
        if (state is null || state.Projects.Count == 0)
        {
            var repo = FindRepoRoot();
            var project = new ProjectViewModel(Slug("workspace") + "-" + DateTime.Now.Ticks, "workspace", repo);
            Projects.Add(project);
            ActiveProject = project;
            return;
        }

        _claudePath = state.Settings.ClaudePath;
        _codexPath = state.Settings.CodexPath;
        _ollamaEndpoint = string.IsNullOrWhiteSpace(state.Settings.OllamaEndpoint) ? _ollamaEndpoint : state.Settings.OllamaEndpoint;
        _ollamaModel = string.IsNullOrWhiteSpace(state.Settings.OllamaModel) ? _ollamaModel : state.Settings.OllamaModel;
        TranslationEnabled = state.Settings.TranslationEnabled;
        MaxConcurrentSessions = state.Settings.MaxConcurrentSessions;
        _isReviewOpen = state.Settings.ReviewPaneOpen;
        _warnNoWorktree = state.Settings.WarnNoWorktree;
        _approvalPolicy = state.Settings.ApprovalPolicy is "ask" or "safe" ? state.Settings.ApprovalPolicy : "yolo";
        _worktreeBase = (state.Settings.WorktreeBase ?? "").Trim();
        _autoStartLastSession = state.Settings.AutoStartLastSession;
        _streamLogs = state.Settings.StreamLogs;
        _defaultModels = state.Settings.DefaultModels ?? new();
        _accent = Theme.AccentPalette.Normalize(state.Settings.Accent);
        _density = state.Settings.Density == "compact" ? "compact" : "comfortable";
        _telemetry = state.Settings.Telemetry;
        _disabledEngines.Clear();
        foreach (var d in state.Settings.DisabledEngines ?? []) _disabledEngines.Add(d);
        _engineAuthMode.Clear();
        foreach (var kv in state.Settings.EngineAuthMode ?? new()) _engineAuthMode[kv.Key] = kv.Value;
        _engineApiKey.Clear();
        foreach (var kv in state.Settings.EngineApiKey ?? new()) _engineApiKey[kv.Key] = kv.Value;
        _theme = string.IsNullOrWhiteSpace(state.Settings.Theme) ? "dark" : state.Settings.Theme;
        _language = state.Settings.Language == "en" ? "en" : "ko";
        _translator = CreateTranslator(_ollamaEndpoint, _ollamaModel);

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
    }

    private void SaveState()
    {
        try
        {
            AppStateStore.Save(new AppStateDto
            {
                ActiveProjectId = ActiveProject?.Id,
                Settings = new AppSettingsDto
                {
                    ClaudePath = _claudePath,
                    CodexPath = _codexPath,
                    OllamaEndpoint = _ollamaEndpoint,
                    OllamaModel = _ollamaModel,
                    TranslationEnabled = TranslationEnabled,
                    MaxConcurrentSessions = MaxConcurrentSessions,
                    ReviewPaneOpen = IsReviewOpen,
                    WarnNoWorktree = _warnNoWorktree,
                    Theme = _theme,
                    Language = _language,
                    ApprovalPolicy = _approvalPolicy,
                    WorktreeBase = _worktreeBase,
                    AutoStartLastSession = _autoStartLastSession,
                    StreamLogs = _streamLogs,
                    DefaultModels = new Dictionary<string, string>(_defaultModels),
                    Accent = _accent,
                    Density = _density,
                    Telemetry = _telemetry,
                    DisabledEngines = _disabledEngines.ToList(),
                    EngineAuthMode = new Dictionary<string, string>(_engineAuthMode),
                    EngineApiKey = new Dictionary<string, string>(_engineApiKey),
                },
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

    private static readonly string DefaultWorktreesRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "worktrees");
    /// <summary>설정된 worktree 기준 경로 (빈 값이면 기본 앱 데이터 폴더).</summary>
    private string WorktreesRoot => string.IsNullOrWhiteSpace(_worktreeBase) ? DefaultWorktreesRoot : _worktreeBase.Trim();

    /// <summary>Create the session's worktree once (branched from project HEAD). No-op if not a git repo.</summary>
}
