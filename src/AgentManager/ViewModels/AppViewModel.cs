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

public enum MainViewKind
{
    Orchestrator,
    History,
    Scheduled,
    Settings,
    Session,
}

public sealed partial class AppViewModel : ObservableObject
{
    private OllamaTranslator _translator = CreateTranslator("http://localhost:11434", "exaone3.5:7.8b");
    private static string L(string key, params object?[] args) => AgentManager.App.L(key, args);
    private readonly List<SessionViewModel> _allSessions = [];
    private readonly Dictionary<string, INativeWorkObserver> _nativeObservers = [];
    private readonly DispatcherTimer _runtimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TimerScheduler _scheduler = new();
    private string _claudePath = "";
    private string _codexPath = "";
    private string _ollamaEndpoint = "http://localhost:11434";
    private string _ollamaModel = "exaone3.5:7.8b";

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public ObservableCollection<SessionViewModel> ActiveSessions { get; } = [];
    public ObservableCollection<SessionViewModel> ProjectSessions { get; } = [];
    public ObservableCollection<SessionViewModel> ArchivedSessions { get; } = [];
    /// <summary>워커 위임 풀(활성 프로젝트의 Role=Worker 세션). 사이드바 WORKERS 그룹.</summary>
    public ObservableCollection<SessionViewModel> ProjectWorkers { get; } = [];
    public bool HasProjectWorkers => ProjectWorkers.Count > 0;
    public ObservableCollection<CliHistoryItemViewModel> CliHistory { get; } = [];
    public ObservableCollection<ScheduledJobViewModel> ScheduledJobs { get; } = [];
    public ObservableCollection<HistoryRowViewModel> HistoryRows { get; } = [];
    /// <summary>레지스트리에서 활성화된 전체 엔진 (설정 카드·모델 조회용 — 사용자 비활성과 무관).</summary>
    public EngineDef[] AllEngines { get; } = Array.FindAll(EngineRegistry.All, e => e.Enabled);
    private readonly HashSet<string> _disabledEngines = [];
    /// <summary>New Agent 피커에 노출할 엔진 (사용자가 비활성한 것 제외).</summary>
    public IEnumerable<EngineDef> Engines => Array.FindAll(AllEngines, e => !_disabledEngines.Contains(e.Id));
    public string Project => ActiveProject?.Name ?? "workspace";
    public string WorkingDirectory => ActiveProject?.Path ?? FindRepoRoot();

    public AppViewModel()
    {
        NewAgentSelectedEngine = AllEngines[0];
        RestoreState();
        Theme.AccentPalette.Apply(_accent);
        LoadScheduledJobs();
        CurrentView = MainViewKind.Orchestrator;
        if (_autoStartLastSession && _allSessions.Count > 0)
        {
            ActiveSession = _allSessions[0];
            CurrentView = MainViewKind.Session;
        }
        NewProjectPath = WorkingDirectory;
        _runtimeTimer.Tick += (_, _) => { RefreshRunningSessions(); RefreshQuotaText(); };
        _runtimeTimer.Start();

        NewAgentCommand = new RelayCommand(_ => ShowNewAgent = true);
        CancelNewAgentCommand = new RelayCommand(_ => ShowNewAgent = false);
        CreateSessionCommand = new RelayCommand(_ => CreateSession(), _ => NewAgentSelectedEngine is not null);
        SelectSessionCommand = new RelayCommand(s => { if (s is SessionViewModel vm) ActiveSession = vm; });
        ShowViewCommand = new RelayCommand(p => CurrentView = (p as string) switch
        {
            "history" => MainViewKind.History,
            "scheduled" => MainViewKind.Scheduled,
            "session" => MainViewKind.Session,
            _ => MainViewKind.Orchestrator,
        });
        NewProjectCommand = new RelayCommand(_ => ShowNewProject = true);
        CancelNewProjectCommand = new RelayCommand(_ => ShowNewProject = false);
        ShowAboutCommand = new RelayCommand(_ => ShowAbout = true);
        CloseAboutCommand = new RelayCommand(_ => ShowAbout = false);
        CreateProjectCommand = new RelayCommand(_ => CreateProject(), _ => !string.IsNullOrWhiteSpace(NewProjectPath));
        SelectProjectCommand = new RelayCommand(p => { if (p is ProjectViewModel vm) ActiveProject = vm; });
        ImportCliSessionCommand = new RelayCommand(p => { if (p is CliHistoryItemViewModel h) ImportCliSession(h); });
        ShowSettingsCommand = new RelayCommand(_ => OpenSettings());
        CancelSettingsCommand = new RelayCommand(_ => CloseSettings());
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
        RenameProjectCommand = new RelayCommand(p =>
        {
            if (p is ProjectViewModel proj && !string.IsNullOrWhiteSpace(proj.RenameDraft))
            {
                RenameProject(proj, proj.RenameDraft.Trim());
            }
        });
        RemoveProjectCommand = new RelayCommand(p => { if (p is ProjectViewModel proj) RemoveProject(proj); });
        RemoveExtraPathCommand = new RelayCommand(p =>
        {
            if (p is string path && ActiveProject is { } proj && proj.ExtraPaths.Remove(path))
                SaveState();
        });
        RefreshCliHistoryCommand = new RelayCommand(_ => _ = LoadCliHistoryAsync(ActiveProject));
        CommitReviewCommand = new RelayCommand(_ => _ = CommitReviewAsync(ActiveSession), _ => ActiveSession?.WorktreePath is not null);
        ForkSessionCommand = new RelayCommand(p => ForkSession(p as SessionViewModel ?? ActiveSession), p => (p as SessionViewModel ?? ActiveSession) is not null);
        SendDiffFeedbackCommand = new RelayCommand(p => { if (p is string f) _ = SendDiffFeedbackAsync(ActiveSession, f); },
            _ => ActiveSession is { IsRunning: false, SelectedChange: not null });
        ApproveCommand = new RelayCommand(p => { if (p is ApprovalBlock b) ResolveApproval(b.RequestId, true); });
        ApproveForSessionCommand = new RelayCommand(p => { if (p is ApprovalBlock b) ResolveApproval(b.RequestId, true, forSession: true); });
        DenyCommand = new RelayCommand(p => { if (p is ApprovalBlock b) ResolveApproval(b.RequestId, false); });
        OpenIdeCommand = new RelayCommand(_ => OpenIde(ActiveSession), _ => ActiveSession is not null);
        CheckUsageCommand = new RelayCommand(_ => _ = CheckUsageAsync(), _ => !_checkingUsage);
        RefreshScheduledJobsCommand = new RelayCommand(_ => LoadScheduledJobs());
        NewScheduleCommand = new RelayCommand(_ => OpenNewSchedule(), _ => ActiveProject is not null);
        CancelNewScheduleCommand = new RelayCommand(_ => ShowNewSchedule = false);
        CreateScheduleCommand = new RelayCommand(_ => CreateSchedule(), _ => ActiveProject is not null && !string.IsNullOrWhiteSpace(NewScheduleTitle));
        RefreshHistoryCommand = new RelayCommand(_ => RebuildHistoryRows());
        SetHistoryAgentFilterCommand = new RelayCommand(p => { HistoryAgentFilter = p?.ToString() ?? "all"; });
        SetHistoryStatusFilterCommand = new RelayCommand(p => { HistoryStatusFilter = p?.ToString() ?? "all"; });
        _scheduler.JobDue += Scheduler_JobDue;
        _scheduler.Start();
        InitNavCommands();
        InitDelegationCommands();
        StartSettingsWatcher();
    }

    public RelayCommand OpenIdeCommand { get; }
    public RelayCommand CheckUsageCommand { get; }
    public RelayCommand RefreshScheduledJobsCommand { get; }
    public RelayCommand NewScheduleCommand { get; }
    public RelayCommand CancelNewScheduleCommand { get; }
    public RelayCommand CreateScheduleCommand { get; }
    public RelayCommand RefreshHistoryCommand { get; }
    public RelayCommand SetHistoryAgentFilterCommand { get; }
    public RelayCommand SetHistoryStatusFilterCommand { get; }

    public void Dispose()
    {
        // 창을 닫을 때 진행 중인 세션을 먼저 취소해 엔진 자식 프로세스(cc/gx/ag/agy) 트리를 정리한다.
        // ct.Register(() => proc.Kill(entireProcessTree: true))가 걸려 있어 Cancel()이 프로세스 트리를 죽인다.
        // 비차단(non-blocking)으로 발사만 한다 — UI 스레드에서 절대 .Wait()/.Result로 막지 않는다.
        foreach (var cts in _running.Values.ToArray())
        {
            try { cts.Cancel(); } catch { /* 이미 dispose된 CTS일 수 있음 */ }
        }

        _scheduler.JobDue -= Scheduler_JobDue;
        _scheduler.Dispose();
        _runtimeTimer.Stop();
    }

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
    public RelayCommand ApproveForSessionCommand { get; }
    public RelayCommand DenyCommand { get; }

    private Task<PermissionDecision> HandlePermissionAsync(SessionViewModel s, PermissionRequest pr)
    {
        var tcs = new TaskCompletionSource<PermissionDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        Application.Current.Dispatcher.Invoke(() =>
        {
            var summary = pr.InputJson.Length > 400 ? pr.InputJson[..400] + "…" : pr.InputJson;
            var block = new ApprovalBlock(pr.RequestId, pr.ToolName, summary)
            {
                SupportsSessionApproval = s.IsCodex, // codex app-server만 acceptForSession 지원
            };
            s.Transcript.Add(block);
            _pendingApprovals[pr.RequestId] = (tcs, block, s);
            s.Status = "waiting";
            s.Activity = L("L.WaitingApproval", pr.ToolName);
            AttentionRequested?.Invoke("approval", s);
        });
        return tcs.Task;
    }

    public void ResolveApproval(string requestId, bool allow, bool forSession = false)
    {
        if (!_pendingApprovals.Remove(requestId, out var entry)) return;
        entry.Block.State = allow ? (forSession ? "allowed (session)" : "allowed") : "denied";
        entry.Session.Status = "running";
        entry.Session.Activity = allow ? L("L.ApprovalGrantedContinue", entry.Block.ToolName) : L("L.ApprovalRejected", entry.Block.ToolName);
        entry.Tcs.TrySetResult(new PermissionDecision(allow, allow ? null : L("L.PermissionDenied"), forSession));
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
                entry.Tcs.TrySetResult(new PermissionDecision(false, L("L.SessionEnded")));
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
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand CloseAboutCommand { get; }
    public RelayCommand CreateProjectCommand { get; }
    public RelayCommand SelectProjectCommand { get; }
    public RelayCommand ImportCliSessionCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand ShowViewCommand { get; }
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
    public RelayCommand RenameProjectCommand { get; }
    public RelayCommand RemoveProjectCommand { get; }
    public RelayCommand RemoveExtraPathCommand { get; }
    public RelayCommand RefreshCliHistoryCommand { get; }
    public RelayCommand CommitReviewCommand { get; }
    public RelayCommand ForkSessionCommand { get; }
    public RelayCommand SendDiffFeedbackCommand { get; }

    /// <summary>확인 다이얼로그 시임(View가 주입). null이면 무조건 진행(헤드리스/테스트).</summary>
    public IDialogService? Dialogs { get; set; }

    /// <summary>동시 실행 세션 수 제한 (설정, 영속).</summary>
    private int _maxConcurrentSessions = 3;
    public int MaxConcurrentSessions
    {
        get => _maxConcurrentSessions;
        set => Set(ref _maxConcurrentSessions, Math.Max(1, value));
    }

    /// <summary>워커 전용 동시 실행 cap (메인 cap과 분리, 설정·영속).</summary>
    private int _maxConcurrentWorkers = AgentManager.Core.Workers.WorkerDefaults.DefaultMaxConcurrentWorkers;
    public int MaxConcurrentWorkers
    {
        get => _maxConcurrentWorkers;
        set => Set(ref _maxConcurrentWorkers, Math.Max(1, value));
    }

    /// <summary>새 워커 기본 행동 규칙 preamble 템플릿 (설정·영속). 빈값이면 기본 템플릿 사용.</summary>
    private string _workerBehaviorPreamble = AgentManager.Core.Workers.WorkerDefaults.BehaviorPreamble;
    public string WorkerBehaviorPreamble
    {
        get => _workerBehaviorPreamble;
        set => Set(ref _workerBehaviorPreamble, value);
    }

    /// <summary>Commit-only: 에이전트 브랜치에 커밋만 하고 머지하지 않음(리뷰 보존).</summary>
    public async Task CommitReviewAsync(SessionViewModel? s)
    {
        if (s?.WorktreePath is null) return;
        s.ReviewStatus = "Committing…";
        var (ok, msg) = await GitWorktree.CommitAsync(s.WorktreePath, $"agent: {s.Title}");
        s.Transcript.Add(ok ? new WorkingBlock("✓ " + msg) : (TranscriptItem)new ErrorBlock(L("L.CommitFailed"), msg));
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
            s.Transcript.Add(new WorkingBlock(L("L.ForkedFrom", src.Title)));
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

    /// <summary>멀티폴더: 활성 프로젝트에 추가 루트 폴더 등록 (중복/주 폴더 제외).</summary>
    public void AddExtraPath(string path)
    {
        if (ActiveProject is not { } proj) return;
        var full = Path.GetFullPath(path).TrimEnd('\\', '/');
        if (string.Equals(full, Path.GetFullPath(proj.Path).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
        if (proj.ExtraPaths.Any(p => string.Equals(Path.GetFullPath(p).TrimEnd('\\', '/'), full, StringComparison.OrdinalIgnoreCase))) return;
        proj.ExtraPaths.Add(full);
        SaveState();
    }

    public void RenameProject(ProjectViewModel proj, string newName)
    {
        proj.Name = newName.Trim();
        if (ReferenceEquals(ActiveProject, proj))
        {
            OnChanged(nameof(Project));
        }
        SaveState();
    }

    public void RemoveProject(ProjectViewModel? p)
    {
        if (p is null) return;

        var sessionsToRemove = _allSessions.Where(s => s.ProjectId == p.Id).ToList();
        if (sessionsToRemove.Any() && Dialogs is { } d && !d.Confirm(L("L.ProjectRemoveConfirm", sessionsToRemove.Count), L("L.ProjectRemoveTitle")))
            return;

        foreach (var s in sessionsToRemove)
        {
            if (_running.TryGetValue(s.Id, out var cts))
            {
                try { cts.Cancel(); } catch { }
            }
            s.PropertyChanged -= SessionStatusWatch;
            _allSessions.Remove(s);
        }

        Projects.Remove(p);

        if (ReferenceEquals(ActiveProject, p))
        {
            ActiveProject = Projects.FirstOrDefault();
        }

        RefreshProjectSessions();
        RefreshCounts();
        RefreshProjectCounts();
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

    private bool _isReviewOpen = true;
    public bool IsReviewOpen
    {
        get => _isReviewOpen;
        set { if (Set(ref _isReviewOpen, value)) { OnChanged(nameof(ReviewPaneWidth)); SaveState(); } }
    }
    public GridLength ReviewPaneWidth => IsSessionView && IsReviewOpen ? new GridLength(420) : new GridLength(0);

    private MainViewKind _currentView = MainViewKind.Orchestrator;
    public MainViewKind CurrentView
    {
        get => _currentView;
        set
        {
            if (!Set(ref _currentView, value)) return;
            OnChanged(nameof(IsOrchestratorView));
            OnChanged(nameof(IsHistoryView));
            OnChanged(nameof(IsScheduledView));
            OnChanged(nameof(IsSettingsView));
            OnChanged(nameof(IsSessionView));
            OnChanged(nameof(ShowSessionPane));
            OnChanged(nameof(ShowSessionEmpty));
            OnChanged(nameof(ReviewPaneWidth));
            if (value == MainViewKind.History)
                RebuildHistoryRows();
        }
    }

    public bool IsOrchestratorView => CurrentView == MainViewKind.Orchestrator;
    public bool IsHistoryView => CurrentView == MainViewKind.History;
    public bool IsScheduledView => CurrentView == MainViewKind.Scheduled;
    public bool IsSettingsView => CurrentView == MainViewKind.Settings;
    public bool IsSessionView => CurrentView == MainViewKind.Session;
    public bool ShowSessionPane => IsSessionView && HasActive;
    public bool ShowSessionEmpty => IsSessionView && !HasActive;

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
                OnChanged(nameof(ShowSessionPane));
                OnChanged(nameof(ShowSessionEmpty));
                RefreshQuotaText(); // footer를 선택된 엔진의 잔여량으로 갱신
                if (value is not null)
                    CurrentView = MainViewKind.Session;
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
                _ = LoadCliHistoryAsync(value);
                SaveState();
            }
        }
    }

    // ----- new-agent overlay state -----
    private bool _showNew;
    public bool ShowNewAgent { get => _showNew; set => Set(ref _showNew, value); }
    private EngineDef? _newEngine;
    public EngineDef? NewAgentSelectedEngine
    {
        get => _newEngine;
        set
        {
            if (Set(ref _newEngine, value))
            {
                OnChanged(nameof(NewAgentModels));
                OnChanged(nameof(NewAgentBranchPreview));
                NewAgentModel = value is { } e ? DefaultModelFor(e.Id) : "";
            }
        }
    }
    public string[] NewAgentModels => _newEngine?.Models ?? [];
    private string _newAgentModel = "";
    public string NewAgentModel { get => _newAgentModel; set => Set(ref _newAgentModel, value); }
    /// <summary>현재 task 기준 생성될 worktree 브랜치 미리보기.</summary>
    public string NewAgentBranchPreview =>
        "agent/" + Slug(string.IsNullOrWhiteSpace(_newTitle) ? "task" : _newTitle);
    private string _newTitle = "";
    public string NewAgentTitle
    {
        get => _newTitle;
        set { if (Set(ref _newTitle, value)) OnChanged(nameof(NewAgentBranchPreview)); }
    }

    // ----- about overlay state -----
    private bool _showAbout;
    public bool ShowAbout { get => _showAbout; set => Set(ref _showAbout, value); }

    // ----- new-project overlay state -----
    private bool _showNewProject;
    public bool ShowNewProject { get => _showNewProject; set => Set(ref _showNewProject, value); }
    private string _newProjectName = "";
    public string NewProjectName { get => _newProjectName; set => Set(ref _newProjectName, value); }
    private string _newProjectPath = "";
    public string NewProjectPath { get => _newProjectPath; set => Set(ref _newProjectPath, value); }
    private string _newProjectError = "";
    public string NewProjectError { get => _newProjectError; set => Set(ref _newProjectError, value); }

    private void CreateSession()
    {
        var project = ActiveProject ?? Projects.FirstOrDefault();
        if (project is null) return;
        var engine = NewAgentSelectedEngine ?? Engines.FirstOrDefault() ?? AllEngines[0];
        var title = string.IsNullOrWhiteSpace(NewAgentTitle) ? $"New {engine.Name} task" : NewAgentTitle.Trim();
        var branch = "agent/" + Slug(title);
        var (reqAppr, sandbox) = PolicyToSession(_approvalPolicy);
        // 모달에서 고른 모델 우선 (유효할 때), 없으면 엔진 기본
        var model = !string.IsNullOrWhiteSpace(NewAgentModel) && Array.IndexOf(engine.Models, NewAgentModel) >= 0
            ? NewAgentModel
            : (DefaultModelFor(engine.Id) is { Length: > 0 } dm ? dm : engine.Models[0]);
        var s = new SessionViewModel("s" + DateTime.Now.Ticks, engine, title, branch, project.Id, project.Name, project.Path, model)
        {
            TranslationEnabled = TranslationEnabled,
            RequireApproval = reqAppr,
            Sandbox = sandbox,
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

    /// <summary>AgentManager 밖에서 돌린 claude/codex CLI 세션 기록을 프로젝트별로 발견해 표시.
    /// 이미 가져온(EngineSessionId 일치) 항목은 숨긴다.</summary>
    private void CreateProject()
    {
        var path = NewProjectPath.Trim().Trim('"');
        if (!Path.IsPathRooted(path))
        {
            NewProjectError = L("L.RequireFullPath");
            return;
        }
        if (!Directory.Exists(path))
        {
            try { Directory.CreateDirectory(path); }
            catch (Exception ex)
            {
                NewProjectError = L("L.FolderCreateFailed", ex.Message);
                return;
            }
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
        if (string.IsNullOrWhiteSpace(name)) name = L("L.DefaultProjectName");

        var project = new ProjectViewModel(Slug(name) + "-" + DateTime.Now.Ticks, name, fullPath);
        Projects.Add(project);
        ActiveProject = project;
        ShowNewProject = false;
        NewProjectName = "";
        NewProjectPath = "";
        NewProjectError = "";
        SaveState();
    }

    private void SessionStatusWatch(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.Status) && sender is SessionViewModel s)
        {
            if (s.Status == "running")
            {
                lock (_scannedSessionDiffIds)
                {
                    _scannedSessionDiffIds.Remove(s.Id);
                }
                s.DiffAdded = 0;
                s.DiffRemoved = 0;
                s.DiffFiles = 0;
            }
            else if (s.Status is "done" or "error")
            {
                lock (_scannedSessionDiffIds)
                {
                    _scannedSessionDiffIds.Remove(s.Id);
                }
            }
            RefreshProjectSessions();
            RefreshCounts();
            if (IsHistoryView) RebuildHistoryRows();
        }
        else if (e.PropertyName == nameof(SessionViewModel.TranslationEnabled))
        {
            SaveState();
        }
    }

    private string _sessionFilter = "";
    public string SessionFilter
    {
        get => _sessionFilter;
        set
        {
            if (Set(ref _sessionFilter, value ?? ""))
            {
                RefreshProjectSessions(selectFirstIfMissing: false);
            }
        }
    }

    private void RefreshProjectSessions(bool selectFirstIfMissing = true)
    {
        var current = ActiveSession;
        Sessions.Clear();
        ActiveSessions.Clear();
        ProjectSessions.Clear();
        ArchivedSessions.Clear();
        ProjectWorkers.Clear();
        if (ActiveProject is { } project)
        {
            var filtered = _allSessions.Where(s => s.ProjectId == project.Id);
            if (!string.IsNullOrWhiteSpace(_sessionFilter))
            {
                var filter = _sessionFilter.Trim();
                filtered = filtered.Where(s =>
                    (s.Title != null && s.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    (s.Branch != null && s.Branch.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    (s.Project != null && s.Project.Contains(filter, StringComparison.OrdinalIgnoreCase))
                );
            }

            foreach (var s in filtered)
            {
                Sessions.Add(s);
                if (s.IsWorker) ProjectWorkers.Add(s);          // 워커는 전용 WORKERS 그룹으로
                else if (s.IsArchived) ArchivedSessions.Add(s);
                else if (s.IsLive) ActiveSessions.Add(s);
                else ProjectSessions.Add(s);
            }
        }

        OnChanged(nameof(HasProjectWorkers));
        RefreshProjectCounts();

        if (current is not null && Sessions.Contains(current))
            ActiveSession = current;
        else if (selectFirstIfMissing)
            ActiveSession = Sessions.FirstOrDefault();
        RefreshCounts();

        _ = Task.Run(async () => await ScanSessionDiffsBackgroundAsync(ProjectSessions.Concat(ActiveSessions).Concat(ArchivedSessions).ToList()));
    }

    private void RefreshProjectCounts()
    {
        foreach (var p in Projects)
            p.SessionCount = _allSessions.Count(s => s.ProjectId == p.Id);
    }

}
