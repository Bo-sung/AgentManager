using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed class AppViewModel : ObservableObject
{
    private OllamaTranslator _translator = CreateTranslator("http://localhost:11434", "exaone3.5:7.8b");
    private readonly List<SessionViewModel> _allSessions = [];
    private readonly DispatcherTimer _runtimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _claudePath = "";
    private string _codexPath = "";
    private string _ollamaEndpoint = "http://localhost:11434";
    private string _ollamaModel = "exaone3.5:7.8b";

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public ObservableCollection<SessionViewModel> ActiveSessions { get; } = [];
    public ObservableCollection<SessionViewModel> ProjectSessions { get; } = [];
    public ObservableCollection<SessionViewModel> ArchivedSessions { get; } = [];
    public EngineDef[] Engines { get; } = Array.FindAll(EngineRegistry.All, e => e.Enabled);
    public string Project => ActiveProject?.Name ?? "workspace";
    public string WorkingDirectory => ActiveProject?.Path ?? FindRepoRoot();

    public AppViewModel()
    {
        NewAgentSelectedEngine = Engines[0];
        RestoreState();
        NewProjectPath = WorkingDirectory;
        _runtimeTimer.Tick += (_, _) => RefreshRunningSessions();
        _runtimeTimer.Start();

        NewAgentCommand = new RelayCommand(_ => ShowNewAgent = true);
        CancelNewAgentCommand = new RelayCommand(_ => ShowNewAgent = false);
        CreateSessionCommand = new RelayCommand(_ => CreateSession(), _ => NewAgentSelectedEngine is not null);
        SelectSessionCommand = new RelayCommand(s => { if (s is SessionViewModel vm) ActiveSession = vm; });
        NewProjectCommand = new RelayCommand(_ => ShowNewProject = true);
        CancelNewProjectCommand = new RelayCommand(_ => ShowNewProject = false);
        CreateProjectCommand = new RelayCommand(_ => CreateProject(), _ => !string.IsNullOrWhiteSpace(NewProjectPath));
        SelectProjectCommand = new RelayCommand(p => { if (p is ProjectViewModel vm) ActiveProject = vm; });
        ShowSettingsCommand = new RelayCommand(_ => OpenSettings());
        CancelSettingsCommand = new RelayCommand(_ => ShowSettings = false);
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => ActiveSession?.CanSend == true);
        StopCommand = new RelayCommand(_ => StopActive(), _ => ActiveSession?.IsRunning == true);
        RefreshReviewCommand = new RelayCommand(_ => _ = RefreshReviewAsync(ActiveSession), _ => ActiveSession is not null);
        ToggleReviewCommand = new RelayCommand(_ => IsReviewOpen = !IsReviewOpen);
        MergeReviewCommand = new RelayCommand(_ => _ = MergeReviewAsync(ActiveSession), _ => ActiveSession?.WorktreePath is not null);
        DiscardReviewCommand = new RelayCommand(_ => _ = DiscardReviewAsync(ActiveSession), _ => ActiveSession?.WorktreePath is not null);
        DeleteSessionCommand = new RelayCommand(p => _ = DeleteSessionAsync(p as SessionViewModel ?? ActiveSession), p => (p as SessionViewModel ?? ActiveSession) is not null);
        ArchiveSessionCommand = new RelayCommand(p => ToggleArchive(p as SessionViewModel ?? ActiveSession), p => (p as SessionViewModel ?? ActiveSession) is not null);
        RenameSessionCommand = new RelayCommand(p => { if (p is string t) RenameSession(ActiveSession, t); }, _ => ActiveSession is not null);
        CommitReviewCommand = new RelayCommand(_ => _ = CommitReviewAsync(ActiveSession), _ => ActiveSession?.WorktreePath is not null);
        ForkSessionCommand = new RelayCommand(p => ForkSession(p as SessionViewModel ?? ActiveSession), p => (p as SessionViewModel ?? ActiveSession) is not null);
        SendDiffFeedbackCommand = new RelayCommand(p => { if (p is string f) _ = SendDiffFeedbackAsync(ActiveSession, f); },
            _ => ActiveSession is { IsRunning: false, SelectedChange: not null });
        ApproveCommand = new RelayCommand(p => { if (p is ApprovalBlock b) ResolveApproval(b.RequestId, true); });
        DenyCommand = new RelayCommand(p => { if (p is ApprovalBlock b) ResolveApproval(b.RequestId, false); });
        OpenIdeCommand = new RelayCommand(_ => OpenIde(ActiveSession), _ => ActiveSession is not null);
    }

    public RelayCommand OpenIdeCommand { get; }

    /// <summary>IDE 핸드오프: 활성 세션의 worktree(없으면 프로젝트 폴더)를 VS Code로 연다.
    /// ShellExecute는 .cmd(code)를 못 찾으므로 설치 경로를 직접 탐색해 cmd /c로 실행. 없으면 탐색기.</summary>
    private static void OpenIde(SessionViewModel? s)
    {
        if (s is null) return;
        var path = s.WorktreePath ?? s.ProjectPath;
        if (!Directory.Exists(path)) return;

        var code = FindVsCodeCli();
        try
        {
            if (code is not null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{code}\" \"{path}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return;
            }
        }
        catch { }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static string? FindVsCodeCli()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "bin", "code.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "bin", "code.cmd"),
        ];
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        // PATH lookup (covers custom installs)
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(dir.Trim(), "code.cmd");
            try { if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    /// <summary>Attention signal for the View (taskbar flash/sound when the window is unfocused).
    /// Reasons: "approval" (input needed now), "done", "error".</summary>
    public event Action<string, SessionViewModel>? AttentionRequested;

    // ----- approval broker (Stage 1: Claude) -----
    private readonly Dictionary<string, (TaskCompletionSource<PermissionDecision> Tcs, ApprovalBlock Block, SessionViewModel Session)> _pendingApprovals = [];
    public RelayCommand ApproveCommand { get; }
    public RelayCommand DenyCommand { get; }

    private Task<PermissionDecision> HandlePermissionAsync(SessionViewModel s, PermissionRequest pr)
    {
        var tcs = new TaskCompletionSource<PermissionDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.Invoke(() =>
        {
            var summary = pr.InputJson.Length > 400 ? pr.InputJson[..400] + "…" : pr.InputJson;
            var block = new ApprovalBlock(pr.RequestId, pr.ToolName, summary);
            s.Transcript.Add(block);
            _pendingApprovals[pr.RequestId] = (tcs, block, s);
            s.Status = "waiting";
            s.Activity = $"승인 대기: {pr.ToolName}";
            AttentionRequested?.Invoke("approval", s);
        });
        return tcs.Task;
    }

    public void ResolveApproval(string requestId, bool allow)
    {
        if (!_pendingApprovals.Remove(requestId, out var entry)) return;
        entry.Block.State = allow ? "allowed" : "denied";
        entry.Session.Status = "running";
        entry.Session.Activity = allow ? $"{entry.Block.ToolName} 승인됨 — 계속" : $"{entry.Block.ToolName} 거부됨";
        entry.Tcs.TrySetResult(new PermissionDecision(allow, allow ? null : "User denied permission"));
        SaveState();
    }

    /// <summary>턴 종료/중지 시 남은 승인 요청은 거부로 정리(엔진은 이미 종료됨).</summary>
    private void ExpirePendingApprovals(SessionViewModel s)
    {
        foreach (var key in _pendingApprovals.Where(kv => ReferenceEquals(kv.Value.Session, s)).Select(kv => kv.Key).ToList())
        {
            if (_pendingApprovals.Remove(key, out var entry))
            {
                entry.Block.State = "expired";
                entry.Tcs.TrySetResult(new PermissionDecision(false, "Session ended"));
            }
        }
    }

    // running sessions → their cancellation source (Stop)
    private readonly Dictionary<string, CancellationTokenSource> _running = [];

    // ----- commands -----
    public RelayCommand NewAgentCommand { get; }
    public RelayCommand CancelNewAgentCommand { get; }
    public RelayCommand CreateSessionCommand { get; }
    public RelayCommand SelectSessionCommand { get; }
    public RelayCommand NewProjectCommand { get; }
    public RelayCommand CancelNewProjectCommand { get; }
    public RelayCommand CreateProjectCommand { get; }
    public RelayCommand SelectProjectCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand CancelSettingsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshReviewCommand { get; }
    public RelayCommand ToggleReviewCommand { get; }
    public RelayCommand MergeReviewCommand { get; }
    public RelayCommand DiscardReviewCommand { get; }
    public RelayCommand DeleteSessionCommand { get; }
    public RelayCommand ArchiveSessionCommand { get; }
    public RelayCommand RenameSessionCommand { get; }
    public RelayCommand CommitReviewCommand { get; }
    public RelayCommand ForkSessionCommand { get; }
    public RelayCommand SendDiffFeedbackCommand { get; }

    /// <summary>동시 실행 세션 수 제한 (설정, 영속).</summary>
    private int _maxConcurrentSessions = 3;
    public int MaxConcurrentSessions
    {
        get => _maxConcurrentSessions;
        set => Set(ref _maxConcurrentSessions, Math.Max(1, value));
    }

    /// <summary>Commit-only: 에이전트 브랜치에 커밋만 하고 머지하지 않음(리뷰 보존).</summary>
    public async Task CommitReviewAsync(SessionViewModel? s)
    {
        if (s?.WorktreePath is null) return;
        s.ReviewStatus = "Committing…";
        var (ok, msg) = await GitWorktree.CommitAsync(s.WorktreePath, $"agent: {s.Title}");
        s.Transcript.Add(ok ? new WorkingBlock("✓ " + msg) : (TranscriptItem)new ErrorBlock("Commit 실패", msg));
        await RefreshReviewAsync(s);
        SaveState();
    }

    /// <summary>Fork: 트랜스크립트·엔진세션id를 상속한 새 세션(새 worktree). 다음 턴은 같은 대화에서 분기.</summary>
    public void ForkSession(SessionViewModel? src)
    {
        if (src is null) return;
        var engine = EngineRegistry.Get(src.AgentId);
        var title = src.Title + " (fork)";
        var s = new SessionViewModel("s" + DateTime.Now.Ticks, engine, title, "agent/" + Slug(title),
            src.ProjectId, src.Project, src.ProjectPath, src.Model)
        {
            TranslationEnabled = src.TranslationEnabled,
            EngineSessionId = src.EngineSessionId, // resume the same engine conversation, then diverge
            Sandbox = src.Sandbox,
        };
        foreach (var item in src.Transcript)
            s.Transcript.Add(CloneTranscriptItem(item));
        s.Transcript.Add(new WorkingBlock($"⑂ '{src.Title}'에서 분기됨"));
        s.PropertyChanged += SessionStatusWatch;
        _allSessions.Insert(0, s);
        ActiveSession = s;
        RefreshProjectSessions(selectFirstIfMissing: false);
        RefreshCounts();
        RefreshProjectCounts();
        SaveState();
    }

    private static TranscriptItem CloneTranscriptItem(TranscriptItem item) => item switch
    {
        UserBlock u => new UserBlock(u.Text),
        AgentTextBlock a => new AgentTextBlock(a.Text) { OriginalText = a.OriginalText },
        ToolBlock t => new ToolBlock(t.ToolUseId, t.Kind, t.Name) { Stat = t.Stat, Body = t.Body, OriginalBody = t.OriginalBody },
        ErrorBlock e => new ErrorBlock(e.Title, e.Body),
        WorkingBlock w => new WorkingBlock(w.Text),
        _ => new WorkingBlock(item.ToString() ?? ""),
    };

    /// <summary>Diff 인라인 피드백: 선택한 변경의 diff를 맥락으로 붙여 에이전트에 수정 재지시.</summary>
    public async Task SendDiffFeedbackAsync(SessionViewModel? s, string feedback)
    {
        if (s is null || s.SelectedChange is null || string.IsNullOrWhiteSpace(feedback)) return;
        var diff = s.DiffText ?? "";
        if (diff.Length > 6000) diff = diff[..6000] + "\n... (diff truncated)";
        var prompt =
            $"Review feedback on your change to `{s.SelectedChange.Path}`.\n\n" +
            $"```diff\n{diff}\n```\n\n" +
            $"Feedback: {feedback.Trim()}\n\n" +
            "Apply the feedback to this file (and only what the feedback requires).";
        await RunTurnAsync(s, prompt);
    }

    // ----- session lifecycle (logic; UI hookup deferred) -----

    /// <summary>Delete: stop if running, drop the worktree, remove from all lists, persist.</summary>
    public async Task DeleteSessionAsync(SessionViewModel? s)
    {
        if (s is null) return;
        if (_running.TryGetValue(s.Id, out var cts)) { try { cts.Cancel(); } catch { } }
        if (s.WorktreePath is not null)
        {
            await GitWorktree.RemoveAsync(s.ProjectPath, s.WorktreePath);
            s.WorktreePath = null;
        }
        s.PropertyChanged -= SessionStatusWatch;
        _allSessions.Remove(s);
        if (ReferenceEquals(ActiveSession, s)) ActiveSession = null;
        RefreshProjectSessions();
        RefreshTotals();
        SaveState();
    }

    /// <summary>Archive toggle: hides the session from Active/Project groups (kept in storage).</summary>
    public void ToggleArchive(SessionViewModel? s)
    {
        if (s is null) return;
        s.IsArchived = !s.IsArchived;
        if (s.IsArchived && ReferenceEquals(ActiveSession, s)) ActiveSession = null;
        RefreshProjectSessions();
        SaveState();
    }

    public void RenameSession(SessionViewModel? s, string newTitle)
    {
        if (s is null || string.IsNullOrWhiteSpace(newTitle)) return;
        s.Title = newTitle.Trim();
        SaveState();
    }

    private void StopActive()
    {
        if (ActiveSession is { } s && _running.TryGetValue(s.Id, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
    }

    public string PersistencePath => AppStateStore.StatePath;

    private bool _isReviewOpen;
    public bool IsReviewOpen
    {
        get => _isReviewOpen;
        set { if (Set(ref _isReviewOpen, value)) OnChanged(nameof(ReviewPaneWidth)); }
    }
    public GridLength ReviewPaneWidth => IsReviewOpen ? new GridLength(420) : new GridLength(0);

    // ----- active session -----
    private SessionViewModel? _active;
    public SessionViewModel? ActiveSession
    {
        get => _active;
        set
        {
            if (Set(ref _active, value))
            {
                foreach (var session in _allSessions)
                    session.IsActive = ReferenceEquals(session, value);
                OnChanged(nameof(HasActive));
                _ = RefreshReviewAsync(value);
            }
        }
    }
    public bool HasActive => _active is not null;

    private ProjectViewModel? _activeProject;
    public ProjectViewModel? ActiveProject
    {
        get => _activeProject;
        set
        {
            if (Set(ref _activeProject, value))
            {
                foreach (var project in Projects)
                    project.IsActive = ReferenceEquals(project, value);
                OnChanged(nameof(Project));
                OnChanged(nameof(WorkingDirectory));
                RefreshProjectSessions();
                SaveState();
            }
        }
    }

    // ----- new-agent overlay state -----
    private bool _showNew;
    public bool ShowNewAgent { get => _showNew; set => Set(ref _showNew, value); }
    private EngineDef? _newEngine;
    public EngineDef? NewAgentSelectedEngine { get => _newEngine; set => Set(ref _newEngine, value); }
    private string _newTitle = "";
    public string NewAgentTitle { get => _newTitle; set => Set(ref _newTitle, value); }

    // ----- new-project overlay state -----
    private bool _showNewProject;
    public bool ShowNewProject { get => _showNewProject; set => Set(ref _showNewProject, value); }
    private string _newProjectName = "";
    public string NewProjectName { get => _newProjectName; set => Set(ref _newProjectName, value); }
    private string _newProjectPath = "";
    public string NewProjectPath { get => _newProjectPath; set => Set(ref _newProjectPath, value); }
    private string _newProjectError = "";
    public string NewProjectError { get => _newProjectError; set => Set(ref _newProjectError, value); }

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

    // provider detection status (settings panel)
    public string ClaudeDetectLabel => DetectLabel("cc", _claudePath);
    public string CodexDetectLabel => DetectLabel("gx", _codexPath);
    private static string DetectLabel(string id, string? overridePath)
    {
        var exe = EngineRegistry.ResolveExe(id, id == "cc" ? overridePath : null, id == "gx" ? overridePath : null);
        if (exe is null) return "✗ 미발견";
        if (File.Exists(exe)) return "✓ " + exe;
        return "△ PATH 의존: " + exe; // bare command name — resolved at spawn time
    }
    private void RefreshDetectLabels() { OnChanged(nameof(ClaudeDetectLabel)); OnChanged(nameof(CodexDetectLabel)); }
    private string _settingsStatus = "";
    public string SettingsStatus { get => _settingsStatus; set => Set(ref _settingsStatus, value); }

    // ----- translation + quota -----
    private bool _translationEnabled = true;
    public bool TranslationEnabled { get => _translationEnabled; set => Set(ref _translationEnabled, value); }
    private string _quotaText = "";
    public string QuotaText { get => _quotaText; set => Set(ref _quotaText, value); }

    // ----- counts -----
    public int RunningCount => CountBy("running");
    public int WaitingCount => CountBy("waiting");
    public int DoneCount => CountBy("done");
    private int CountBy(string s) { int n = 0; foreach (var x in Sessions) if (x.Status == s) n++; return n; }
    private void RefreshCounts() { OnChanged(nameof(RunningCount)); OnChanged(nameof(WaitingCount)); OnChanged(nameof(DoneCount)); }

    // ----- aggregate dashboard (all sessions) -----
    public string TotalTokensLabel
    {
        get
        {
            long tin = 0, tout = 0;
            foreach (var x in _allSessions) { tin += x.TokensIn; tout += x.TokensOut; }
            return $"{FmtK(tin)} / {FmtK(tout)}";
        }
    }
    public string TotalCostLabel
    {
        get
        {
            double c = 0;
            foreach (var x in _allSessions) c += x.CostUsd;
            return c > 0 ? "$" + c.ToString("0.00") : "$0";
        }
    }
    private void RefreshTotals() { OnChanged(nameof(TotalTokensLabel)); OnChanged(nameof(TotalCostLabel)); }
    private static string FmtK(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.0") + "M"
        : n >= 1000 ? (n / 1000.0).ToString("0.0") + "k" : n.ToString();

    private void RefreshRunningSessions()
    {
        foreach (var session in _allSessions)
            if (session.IsRunning)
                session.RefreshRuntimeLabels();
    }

    private void CreateSession()
    {
        var project = ActiveProject ?? Projects.FirstOrDefault();
        if (project is null) return;
        var engine = NewAgentSelectedEngine ?? Engines[0];
        var title = string.IsNullOrWhiteSpace(NewAgentTitle) ? $"New {engine.Name} task" : NewAgentTitle.Trim();
        var branch = "agent/" + Slug(title);
        var s = new SessionViewModel("s" + DateTime.Now.Ticks, engine, title, branch, project.Id, project.Name, project.Path, engine.Models[0])
        {
            TranslationEnabled = TranslationEnabled,
        };
        s.PropertyChanged += SessionStatusWatch;
        _allSessions.Insert(0, s);
        ActiveSession = s;
        RefreshProjectSessions(selectFirstIfMissing: false);
        ShowNewAgent = false;
        var task = title;
        NewAgentTitle = "";
        RefreshCounts();
        RefreshProjectCounts();
        SaveState();
        _ = RunTurnAsync(s, task); // first turn = the task
    }

    private void CreateProject()
    {
        var path = NewProjectPath.Trim().Trim('"');
        if (!Directory.Exists(path))
        {
            NewProjectError = "폴더를 찾을 수 없습니다.";
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var existing = Projects.FirstOrDefault(p => string.Equals(p.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveProject = existing;
            ShowNewProject = false;
            NewProjectError = "";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewProjectName)
            ? new DirectoryInfo(fullPath).Name
            : NewProjectName.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "project";

        var project = new ProjectViewModel(Slug(name) + "-" + DateTime.Now.Ticks, name, fullPath);
        Projects.Add(project);
        ActiveProject = project;
        ShowNewProject = false;
        NewProjectName = "";
        NewProjectPath = "";
        NewProjectError = "";
        SaveState();
    }

    private void OpenSettings()
    {
        SettingsClaudePath = _claudePath;
        SettingsCodexPath = _codexPath;
        SettingsOllamaEndpoint = _ollamaEndpoint;
        SettingsOllamaModel = _ollamaModel;
        SettingsDefaultTranslationEnabled = TranslationEnabled;
        SettingsStatus = "";
        ShowSettings = true;
    }

    private void SaveSettings()
    {
        _claudePath = Clean(SettingsClaudePath);
        _codexPath = Clean(SettingsCodexPath);
        RefreshDetectLabels();
        _ollamaEndpoint = string.IsNullOrWhiteSpace(SettingsOllamaEndpoint) ? "http://localhost:11434" : SettingsOllamaEndpoint.Trim();
        _ollamaModel = string.IsNullOrWhiteSpace(SettingsOllamaModel) ? "exaone3.5:7.8b" : SettingsOllamaModel.Trim();
        TranslationEnabled = SettingsDefaultTranslationEnabled;
        _translator = CreateTranslator(_ollamaEndpoint, _ollamaModel);
        SettingsStatus = "Settings saved";
        ShowSettings = false;
        SaveState();
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    private void SessionStatusWatch(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.Status))
        {
            RefreshProjectSessions();
            RefreshCounts();
        }
        else if (e.PropertyName == nameof(SessionViewModel.TranslationEnabled))
        {
            SaveState();
        }
    }

    private void RefreshProjectSessions(bool selectFirstIfMissing = true)
    {
        var current = ActiveSession;
        Sessions.Clear();
        ActiveSessions.Clear();
        ProjectSessions.Clear();
        ArchivedSessions.Clear();
        if (ActiveProject is { } project)
        {
            foreach (var s in _allSessions.Where(s => s.ProjectId == project.Id))
            {
                Sessions.Add(s);
                if (s.IsArchived) ArchivedSessions.Add(s);
                else if (s.IsLive) ActiveSessions.Add(s);
                else ProjectSessions.Add(s);
            }
        }

        RefreshProjectCounts();

        if (current is not null && Sessions.Contains(current))
            ActiveSession = current;
        else if (selectFirstIfMissing)
            ActiveSession = Sessions.FirstOrDefault();
        RefreshCounts();
    }

    private void RefreshProjectCounts()
    {
        foreach (var p in Projects)
            p.SessionCount = _allSessions.Count(s => s.ProjectId == p.Id);
    }

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
        _translator = CreateTranslator(_ollamaEndpoint, _ollamaModel);

        foreach (var p in state.Projects.Where(p => Directory.Exists(p.Path)))
            Projects.Add(new ProjectViewModel(p.Id, p.Name, p.Path) { McpConfigPath = p.McpConfigPath });

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
            var s = new SessionViewModel(
                dto.Id,
                engine,
                dto.Title,
                dto.Branch,
                dto.ProjectId,
                dto.Project,
                dto.ProjectPath,
                dto.Model,
                dto.StartedAt)
            {
                WorktreePath = Directory.Exists(dto.WorktreePath) ? dto.WorktreePath : null,
                Isolated = dto.Isolated && Directory.Exists(dto.WorktreePath),
                WorktreeAttempted = dto.WorktreeAttempted,
                Status = dto.Status is "running" or "waiting" ? "idle" : dto.Status,
                Activity = dto.Status is "running" or "waiting" ? "restored after restart" : dto.Activity,
                TokensIn = dto.TokensIn,
                TokensOut = dto.TokensOut,
                CostUsd = dto.CostUsd,
                IsArchived = dto.IsArchived,
                Sandbox = Enum.TryParse<SandboxMode>(dto.Sandbox, out var sb) ? sb : SandboxMode.DangerFullAccess,
                RequireApproval = dto.RequireApproval,
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
                },
                Projects = Projects.Select(p => new ProjectDto { Id = p.Id, Name = p.Name, Path = p.Path, McpConfigPath = p.McpConfigPath }).ToList(),
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

    private static readonly string WorktreesRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "worktrees");

    /// <summary>Create the session's worktree once (branched from project HEAD). No-op if not a git repo.</summary>
    private async Task EnsureWorktreeAsync(SessionViewModel s)
    {
        if (s.WorktreeAttempted) return;
        s.WorktreeAttempted = true;
        try
        {
            var projectWorktreesRoot = Path.Combine(WorktreesRoot, s.ProjectId);
            Directory.CreateDirectory(projectWorktreesRoot);
            var wt = await GitWorktree.CreateAsync(s.ProjectPath, s.Id, s.Branch, projectWorktreesRoot);
            if (wt is not null) { s.WorktreePath = wt.Path; s.Isolated = true; }
            else s.Transcript.Add(new WorkingBlock("⚠ git 레포가 아니어서 격리(worktree) 없이 실행합니다"));
        }
        catch (Exception ex)
        {
            s.Transcript.Add(new WorkingBlock($"⚠ worktree 생성 실패 — 격리 없이 실행: {ex.Message}"));
        }
    }

    private async Task SendAsync()
    {
        var s = ActiveSession;
        if (s is null || !s.CanSend) return;
        var prompt = s.Draft.Trim();
        s.Draft = "";
        var images = s.PendingImages.ToArray();
        s.PendingImages.Clear();
        await RunTurnAsync(s, prompt, images);
    }

    /// <summary>Run one engine turn for a session and stream normalized events into its transcript.</summary>
    private async Task RunTurnAsync(SessionViewModel s, string prompt, string[]? images = null)
    {
        // concurrency cap: protect the machine/quota from too many parallel engines
        if (_running.Count >= MaxConcurrentSessions)
        {
            s.Transcript.Add(new ErrorBlock("동시 실행 한도",
                $"실행 중 세션이 {_running.Count}개입니다 (한도 {MaxConcurrentSessions}). 끝나길 기다리거나 설정에서 한도를 올리세요."));
            return;
        }

        var dispatcher = Application.Current.Dispatcher;
        s.Transcript.Add(new UserBlock(prompt));
        s.Status = "running";
        s.MarkRunStarted(s.TranslationEnabled ? "translating prompt / preparing run" : "preparing run");

        var adapter = EngineRegistry.CreateAdapter(s.AgentId);
        var exe = EngineRegistry.ResolveExe(s.AgentId, _claudePath, _codexPath);
        if (adapter is null || exe is null)
        {
            s.Transcript.Add(new ErrorBlock("Engine unavailable", $"{s.AgentName} CLI를 찾을 수 없습니다."));
            s.Status = "error";
            s.MarkRunEnded("engine unavailable");
            return;
        }

        // Worktree isolation: each session works in its own git worktree.
        s.Activity = "preparing worktree";
        await EnsureWorktreeAsync(s);
        var cwd = s.WorktreePath ?? s.ProjectPath;

        var tools = new Dictionary<string, ToolBlock>();
        var session = new AgentSession(adapter, exe, _translator, s.TranslationEnabled);
        session.EventReceived += ev => dispatcher.Invoke(() => Apply(s, ev, tools));
        if (s.RequireApproval)
            session.PermissionHandler = pr => HandlePermissionAsync(s, pr);

        // turn baseline for end-of-turn usage reconciliation
        s.TurnBaseIn = s.TokensIn;
        s.TurnBaseOut = s.TokensOut;

        var mcpPath = Projects.FirstOrDefault(p => p.Id == s.ProjectId)?.McpConfigPath;
        var options = new SessionOptions
        {
            WorkingDirectory = cwd,
            BypassPermissions = !s.RequireApproval, // Stage 1: Claude stdio approvals; Codex falls to sandbox
            Sandbox = s.Sandbox,
            ResumeSessionId = s.EngineSessionId,
            Model = string.IsNullOrWhiteSpace(s.Model) ? null : s.Model,
            McpConfigPath = string.IsNullOrWhiteSpace(mcpPath) ? null : mcpPath,
            Images = images ?? [],
        };
        var cts = new CancellationTokenSource();
        _running[s.Id] = cts;
        try
        {
            s.Activity = s.TranslationEnabled ? "translating prompt / starting engine" : "starting engine";
            await Task.Run(() => session.RunAsync(options, prompt, cts.Token), cts.Token);
            if (s.Status == "running") s.Status = "done";
            if (s.Status == "done") s.MarkRunEnded("completed");
        }
        catch (OperationCanceledException)
        {
            s.Transcript.Add(new WorkingBlock("⏹ 중지됨"));
            s.Status = "idle";
            s.MarkRunEnded("stopped");
        }
        catch (Exception ex)
        {
            s.Transcript.Add(new ErrorBlock("Run failed", ex.Message));
            s.Status = "error";
            s.MarkRunEnded("failed");
        }
        finally
        {
            ExpirePendingApprovals(s);
            await RefreshReviewAsync(s);
            SaveState();
            _running.Remove(s.Id);
            cts.Dispose();
        }
    }

    public async Task SelectReviewChangeAsync(ReviewChangeViewModel? change)
    {
        var s = ActiveSession;
        if (s is null) return;
        await LoadReviewDiffAsync(s, change);
    }

    private async Task LoadReviewDiffAsync(SessionViewModel s, ReviewChangeViewModel? change)
    {
        s.SelectedChange = change;
        if (change is null || string.IsNullOrWhiteSpace(s.WorktreePath))
        {
            s.DiffText = "변경 파일을 선택하면 diff가 표시됩니다.";
            return;
        }

        s.DiffText = "Loading diff...";
        try
        {
            var diff = await GitWorktree.GetDiffAsync(s.WorktreePath, change.Path);
            s.DiffText = string.IsNullOrWhiteSpace(diff) ? "No textual diff." : diff;
            SaveState();
        }
        catch (Exception ex)
        {
            s.DiffText = "Diff failed: " + ex.Message;
        }
    }

    /// <summary>실행 중 라이브 갱신: 툴이 끝날 때마다 호출되며, 버스트를 디바운스(0.8s)해
    /// 활성 세션의 Review pane을 갱신한다 (git status는 가볍지만 연타 방지).</summary>
    private bool _liveReviewQueued;
    private async Task QueueLiveReviewRefreshAsync(SessionViewModel s)
    {
        if (_liveReviewQueued || !ReferenceEquals(s, ActiveSession) || s.WorktreePath is null) return;
        _liveReviewQueued = true;
        try
        {
            await Task.Delay(800);
            if (ReferenceEquals(s, ActiveSession))
                await RefreshReviewAsync(s);
        }
        finally { _liveReviewQueued = false; }
    }

    private async Task RefreshReviewAsync(SessionViewModel? s)
    {
        if (s is null) return;
        if (string.IsNullOrWhiteSpace(s.WorktreePath))
        {
            s.Changes.Clear();
            s.SelectedChange = null;
            s.DiffText = "세션 worktree가 아직 없습니다.";
            s.ReviewStatus = "No isolated worktree";
            return;
        }

        s.ReviewStatus = "Scanning changes...";
        try
        {
            var selectedPath = s.SelectedChange?.Path; // keep the user's selection across live refreshes
            var changes = await GitWorktree.GetChangedFilesAsync(s.WorktreePath);
            s.Changes.Clear();
            foreach (var change in changes)
                s.Changes.Add(new ReviewChangeViewModel(change));

            s.ReviewStatus = changes.Count == 0 ? "No changes" : $"{changes.Count} changed file(s)";
            if (s.Changes.Count > 0)
            {
                var keep = selectedPath is null ? null : s.Changes.FirstOrDefault(c => c.Path == selectedPath);
                await LoadReviewDiffAsync(s, keep ?? s.Changes[0]);
            }
            else
            {
                s.SelectedChange = null;
                s.DiffText = "No changes in this worktree.";
            }
        }
        catch (Exception ex)
        {
            s.ReviewStatus = "Review refresh failed";
            s.DiffText = ex.Message;
        }
    }

    private async Task MergeReviewAsync(SessionViewModel? s)
    {
        if (s?.WorktreePath is null) return;
        s.ReviewStatus = "Merging…";
        var (ok, msg) = await GitWorktree.MergeAsync(s.ProjectPath, s.Branch, $"agent: {s.Title}", s.WorktreePath);
        if (ok)
        {
            await GitWorktree.RemoveAsync(s.ProjectPath, s.WorktreePath);
            s.WorktreePath = null;
            s.Isolated = false;
            s.Status = "done";
            s.Activity = "merged";
            s.Transcript.Add(new WorkingBlock("✓ " + msg));
        }
        else
        {
            s.Transcript.Add(new ErrorBlock("Merge 실패", msg));
        }
        s.ReviewStatus = msg;
        await RefreshReviewAsync(s);
        SaveState();
    }

    private async Task DiscardReviewAsync(SessionViewModel? s)
    {
        if (s?.WorktreePath is null) return;
        s.ReviewStatus = "Discarding…";
        var (ok, msg) = await GitWorktree.DiscardAsync(s.WorktreePath);
        s.Transcript.Add(ok ? new WorkingBlock("↩ " + msg) : (TranscriptItem)new ErrorBlock("Discard 실패", msg));
        await RefreshReviewAsync(s);
        SaveState();
    }

    private void Apply(SessionViewModel s, NormalizedEvent ev, Dictionary<string, ToolBlock> tools)
    {
        s.MarkRunSignal();
        switch (ev)
        {
            case SessionStarted started:
                if (!string.IsNullOrWhiteSpace(started.SessionId))
                    s.EngineSessionId = started.SessionId;
                if (!string.IsNullOrWhiteSpace(started.Model))
                    s.MarkRunSignal($"connected · {started.Model}");
                else
                    s.MarkRunSignal("connected");
                break;
            case AssistantText at when !string.IsNullOrWhiteSpace(at.Text):
                s.Transcript.Add(new AgentTextBlock(at.Text) { OriginalText = at.OriginalText });
                s.Activity = "receiving response";
                break;
            case Thinking th when !string.IsNullOrWhiteSpace(th.Text):
                s.Transcript.Add(new ThinkingBlock(th.Text));
                s.Activity = "thinking…";
                break;
            case ToolUseStarted u:
                var tb = new ToolBlock(u.ToolUseId, KindOf(u.Name), u.Name) { CommandText = ExtractCommand(u) };
                tools[u.ToolUseId] = tb;
                s.Transcript.Add(tb);
                s.MarkRunSignal($"{u.Name}…");
                if (u.Name == "TodoWrite")
                    UpsertTaskListArtifact(s, u.InputJson);
                break;
            case ToolResult r:
                if (tools.TryGetValue(r.ToolUseId, out var t))
                {
                    t.Body = Trim(r.Content, 2000);
                    t.OriginalBody = r.OriginalContent is null ? null : Trim(r.OriginalContent, 2000);
                    t.Stat = r.IsError ? "error" : "done";
                    if (t.CommandText is { } cmd && IsTestCommand(cmd))
                        UpsertTestArtifact(s, cmd, r.Content, r.IsError);
                }
                else
                {
                    s.Transcript.Add(new ToolBlock(r.ToolUseId, "RUN", "result")
                    {
                        Body = Trim(r.Content, 2000),
                        OriginalBody = r.OriginalContent is null ? null : Trim(r.OriginalContent, 2000),
                        Stat = r.IsError ? "error" : "done"
                    });
                }
                // live review: a finished tool may have changed files in the worktree
                _ = QueueLiveReviewRefreshAsync(s);
                break;
            case TokenUsage k:
                // live accumulation; TurnCompleted.Usage reconciles to the turn total
                s.TokensIn += k.InputTokens;
                s.TokensOut += k.OutputTokens;
                RefreshTotals();
                break;
            case QuotaUpdate q:
                QuotaText = $"QUOTA {q.Utilization:P0} · {q.RateLimitType}";
                break;
            case EngineError e when !e.Message.Contains("Reading additional input"):
                s.Transcript.Add(new ErrorBlock("stderr", e.Message));
                break;
            case TurnCompleted c:
                if (c.Usage is { } turnUsage)
                {
                    // reconcile: per-message usage undercounts (esp. output); result.usage is the turn total
                    s.TokensIn = s.TurnBaseIn + turnUsage.InputTokens;
                    s.TokensOut = s.TurnBaseOut + turnUsage.OutputTokens;
                }
                if (c.CostUsd is { } turnCost) s.CostUsd += turnCost;
                UpsertSummaryArtifact(s);
                AttentionRequested?.Invoke(c.IsError ? "error" : "done", s);
                s.Status = c.IsError ? "error" : "done";
                s.MarkRunEnded(c.IsError ? "failed" : "completed");
                RefreshTotals();
                break;
        }
        SaveState();
    }

    // ----- artifacts (light): derived from events, no extra engine calls -----

    private static string? ExtractCommand(ToolUseStarted u)
    {
        if (u.Name is not ("Bash" or "shell")) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(u.InputJson);
            return doc.RootElement.TryGetProperty("command", out var c) ? c.GetString() : null;
        }
        catch { return null; }
    }

    private static bool IsTestCommand(string cmd) =>
        System.Text.RegularExpressions.Regex.IsMatch(cmd,
            @"\b(dotnet\s+test|npm\s+(run\s+)?test|pytest|vitest|jest|mocha|cargo\s+test|go\s+test)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static ArtifactViewModel GetOrAddArtifact(SessionViewModel s, string kind, string title)
    {
        var a = s.Artifacts.FirstOrDefault(x => x.Kind == kind && x.Title == title);
        if (a is null) { a = new ArtifactViewModel(kind, title); s.Artifacts.Insert(0, a); }
        return a;
    }

    /// <summary>TodoWrite 입력 → 체크리스트 아티팩트(최신 상태로 교체).</summary>
    private static void UpsertTaskListArtifact(SessionViewModel s, string inputJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(inputJson);
            if (!doc.RootElement.TryGetProperty("todos", out var todos) || todos.ValueKind != System.Text.Json.JsonValueKind.Array) return;
            var lines = new List<string>();
            foreach (var t in todos.EnumerateArray())
            {
                var status = t.TryGetProperty("status", out var st) ? st.GetString() : "";
                var mark = status switch { "completed" => "✅", "in_progress" => "🔄", _ => "⬜" };
                var content = t.TryGetProperty("content", out var ct) ? ct.GetString() : "";
                lines.Add($"{mark} {content}");
            }
            if (lines.Count == 0) return;
            GetOrAddArtifact(s, "tasklist", "Task List").Content = string.Join("\n", lines);
        }
        catch { /* malformed input — skip */ }
    }

    /// <summary>테스트 러너 실행 결과 → 테스트 아티팩트(명령별 최신 결과).</summary>
    private static void UpsertTestArtifact(SessionViewModel s, string cmd, string output, bool isError)
    {
        var shortCmd = cmd.Length > 60 ? cmd[..60] + "…" : cmd;
        var a = GetOrAddArtifact(s, "test", shortCmd);
        a.IsError = isError;
        var tail = output.Length > 1500 ? "…" + output[^1500..] : output;
        a.Content = (isError ? "❌ FAILED\n" : "✅ PASSED\n") + tail;
    }

    /// <summary>턴 종료 시 마지막 어시스턴트 텍스트 → 요약(walkthrough) 아티팩트.</summary>
    private static void UpsertSummaryArtifact(SessionViewModel s)
    {
        var last = s.Transcript.OfType<AgentTextBlock>().LastOrDefault();
        if (last is null || string.IsNullOrWhiteSpace(last.Text)) return;
        GetOrAddArtifact(s, "summary", "Summary").Content = last.Text;
    }

    private static string KindOf(string name) => name switch
    {
        "Read" or "Glob" or "Grep" or "LS" => "READ",
        "Edit" or "MultiEdit" or "Write" => "EDIT",
        _ => "RUN",
    };

    private static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    private static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-')[..Math.Min(28, slug.Trim('-').Length)].TrimEnd('-');
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")))
                return d.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static OllamaTranslator CreateTranslator(string endpoint, string model) =>
        new(new OllamaOptions
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? "exaone3.5:7.8b" : model.Trim(),
        });
}
