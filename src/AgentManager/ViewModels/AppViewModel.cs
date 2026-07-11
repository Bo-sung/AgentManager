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
    private Core.Translation.ITranslator _translator = CreateTranslator("http://localhost:11434", "exaone3.5:7.8b");
    private static string L(string key, params object?[] args) => AgentManager.App.L(key, args);
    private readonly List<SessionViewModel> _allSessions = [];
    /// <summary>사용자가 삭제한 CLI 세션 id — CLI History 재발견에서 영구 제외(삭제가 재시작 후에도 유지).</summary>
    private HashSet<string> _dismissedCliSessions => _settings.DismissedCliSessions;
    private readonly Dictionary<string, INativeWorkObserver> _nativeObservers = [];
    private readonly DispatcherTimer _runtimeTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TimerScheduler _scheduler = new();
    // 일반 설정은 Core SettingsService가 소유; 아래 VM 필드들은 위임 프로퍼티(read/write 모두 서비스로) — overhaul (a) step 2b.
    private readonly AgentManager.Core.Settings.SettingsService _settings = new();
    // 엔진별 지원 모델 + 모델별 추론(effort) 목록의 소스 — %LOCALAPPDATA%\AgentManager\models.json.
    // 하드코딩 대체(사용자가 파일 편집으로 모델/effort 추가·수정). 선택(preferred)은 settings.json 그대로.
    private readonly AgentManager.Core.Models.ModelCatalog _modelCatalog =
        AgentManager.Core.Models.ModelCatalog.Load(AgentManager.Core.Models.DefaultModelCatalog.Build());
    // Per-engine config store (engines/*.json) — the new home for engine-specific settings + custom engines.
    // Seeded/migrated from settings.json + models.json on first run (RestoreState). Read-routing is being moved
    // over incrementally (P1c): efforts already source from here; models/path/auth follow. See docs/ENGINE_CONFIG_OVERHAUL_KO.md.
    private AgentManager.Core.Engines.EngineConfigStore? _engineConfig;
    // Per-engine CLI path override now lives in engines/<id>.json. Read from the store when loaded, else the legacy
    // settings.json holder (used only during startup before the store loads + the one-time migration). Writes go
    // to the store (SetEnginePath). pi-worker keeps its own settings path until it becomes a config engine (P3).
    private string _claudePath { get => _engineConfig?.Get("cc")?.Path ?? _settings.ClaudePath; set => SetEnginePath("cc", value); }
    private string _codexPath { get => _engineConfig?.Get("gx")?.Path ?? _settings.CodexPath; set => SetEnginePath("gx", value); }
    private string _agyPath { get => _engineConfig?.Get("agy")?.Path ?? _settings.AgyPath; set => SetEnginePath("agy", value); }
    private string _piPath { get => _engineConfig?.Get("pi")?.Path ?? _settings.PiPath; set => SetEnginePath("pi", value); }
    private string _piWorkerPath { get => _settings.PiWorkerPath; set => _settings.PiWorkerPath = value; }

    /// <summary>Persist an engine's CLI path override to engines/&lt;id&gt;.json (or the legacy settings holder before
    /// the store is loaded, e.g. during ApplySettings at startup — the migration then folds it into the store).</summary>
    private void SetEnginePath(string id, string value)
    {
        var v = (value ?? "").Trim();
        if (_engineConfig?.Get(id) is { } c) _engineConfig.Upsert(c with { Path = v });
        else switch (id)
        {
            case "cc": _settings.ClaudePath = v; break;
            case "gx": _settings.CodexPath = v; break;
            case "agy": _settings.AgyPath = v; break;
            case "pi": _settings.PiPath = v; break;
        }
    }
    private string _ollamaEndpoint { get => _settings.OllamaEndpoint; set => _settings.OllamaEndpoint = value; }
    private string _ollamaModel { get => _settings.OllamaModel; set => _settings.OllamaModel = value; }

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
    private HashSet<string> _disabledEngines => _settings.DisabledEngines;
    /// <summary>New Agent 피커에 노출할 엔진 (사용자가 비활성한 것 제외).</summary>
    public IEnumerable<EngineDef> Engines => Array.FindAll(AllEngines, e => !_disabledEngines.Contains(e.Id));
    public string Project => ActiveProject?.Name ?? "workspace";
    public string WorkingDirectory => ActiveProject?.Path ?? FindRepoRoot();

    private long _idSeq;
    /// <summary>Collision-safe session id: ticks alone can repeat for rapid creates, so append a
    /// monotonic sequence and verify against live sessions.</summary>
    private string NewSessionId(string prefix)
    {
        string id;
        do { id = prefix + DateTime.Now.Ticks + "-" + (++_idSeq); }
        while (_allSessions.Any(s => s.Id == id));
        return id;
    }

    public AppViewModel()
    {
        InitModelChecklists();
        // 모든 엔진의 컴포저 모델 메뉴가 설정/New-Agent 피커와 동일한 목록(DropdownModelsFor: 동적 카탈로그 +
        // "주로 쓰는 모델" 체크 부분집합 + custom)을 쓰게 한다 — 정적 목록과의 불일치 제거.
        SessionViewModel.ComposerModelsProvider = id => DropdownModelsFor(id);
        // 추론(effort) 옵션·기본값·유무를 엔진별 config(engines/*.json)에서 가져온다(모델별 상이). 아직 로드 전
        // (첫 조회는 RestoreState 이후)이거나 미정의 엔진이면 models.json 카탈로그로 폴백 — 점진 전환(P1c).
        SessionViewModel.EffortOptionsProvider = (eng, model) => _engineConfig?.Get(eng) is { } c ? [.. c.EffortsFor(model)] : [.. _modelCatalog.EffortsFor(eng, model)];
        SessionViewModel.HasEffortProvider = eng => _engineConfig?.Get(eng) is { } c ? c.HasEfforts : _modelCatalog.HasEfforts(eng);
        SessionViewModel.DefaultEffortProvider = (eng, model) => _engineConfig?.Get(eng) is { } c ? c.DefaultEffortFor(model) : _modelCatalog.DefaultEffortFor(eng, model);
        NewAgentSelectedEngine = AllEngines[0];
        RestoreState();
        // 복원된 pi/agy 세션이 있으면 실제 카탈로그를 로드해 컴포저 목록을 채운다(설정을 열지 않아도).
        if (_allSessions.Any(s => s.AgentId == "pi")) _ = QueryPiModelsAsync();
        if (_allSessions.Any(s => s.AgentId == "agy")) _ = QueryAgyModelsAsync();
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
        StartResourceMonitor(); // 1 Hz host CPU/GPU/RAM/Ethernet strip in the titlebar (background-sampled).

        NewAgentCommand = new RelayCommand(_ => ShowNewAgent = true);
        CancelNewAgentCommand = new RelayCommand(_ => ShowNewAgent = false);
        CreateSessionCommand = new RelayCommand(_ => CreateSession(), _ => NewAgentSelectedEngine is not null);
        // Always surface the session view — ActiveSession's setter only switches CurrentView when
        // the value changes, so re-selecting the already-active session must force it here.
        SelectSessionCommand = new RelayCommand(s => { if (s is SessionViewModel vm) { ActiveSession = vm; CurrentView = MainViewKind.Session; } });
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
        OpenProjectFolderCommand = new RelayCommand(p => { if (p is ProjectViewModel vm) OpenProjectFolder(vm.Path); });
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
        RenameSessionCommand = new RelayCommand(p => { if (p is SessionViewModel s && !string.IsNullOrWhiteSpace(s.RenameDraft)) RenameSession(s, s.RenameDraft.Trim()); }, _ => true);
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
        OpenInTerminalCommand = new RelayCommand(_ => OpenInTerminal(ActiveSession), _ => ActiveSession is not null);
        ResyncTranscriptCommand = new RelayCommand(_ => _ = ResyncTranscriptAsync(ActiveSession),
            _ => ActiveSession is { AgentId: "cc" or "gx" or "pi" } s && !string.IsNullOrEmpty(s.EngineSessionId));
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
        InitWorkerTaskCommands();
        StartSettingsWatcher();
        StartTaskSpoolWatcher();          // 스킬이 스풀로 등록한 워커 작업을 백로그로 수신
        _ = RefreshOllamaStatusAsync();   // 시작 시 Ollama 상태 1회 확인(번역 ON 토글 활성 판단)
    }

    public RelayCommand OpenIdeCommand { get; }
    /// <summary>agy를 외부 터미널에서 인터랙티브로 실행(ConPTY 트랜스크립트의 escape hatch).
    /// agy 세션에서만 활성화(CanExecute).</summary>
    public RelayCommand OpenInTerminalCommand { get; }
    public RelayCommand ResyncTranscriptCommand { get; }
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
        _runs.CancelAll();
        StopSpawnedOllama(); // reap the ollama WE started (never an external one) so it can't orphan + lock …\current\

        _scheduler.JobDue -= Scheduler_JobDue;
        _scheduler.Dispose();
        _runtimeTimer.Stop();
        _resourceMonitor.Dispose();
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

    /// <summary>Reveal a project's folder in Explorer (context-menu "open project folder").</summary>
    private static void OpenProjectFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
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

    /// <summary>agy를 외부 터미널에서 인터랙티브(비 -p)로 실행 — ConPTY 캡처 대신 진짜 콘솔에서 풀 TUI/인증 확보.
    /// cwd = worktree(존재 시) 또는 프로젝트 루트; conversation은 agy 캐시 조회로 resume(--conversation id),
    /// 못 찾으면 --continue(이 cwd의 마지막 대화). 터미널은 wt.exe 우선, 없으면 cmd /k 폴백. 실패 시 트랜스크립트 안내.
    /// 스파이크: AGENTMANAGER_* env는 전달하지 않는다(wt.exe 인자로 env 주입이 까다로워 이번엔 생략).</summary>
    /// <summary>Open THIS session in a real external terminal (Windows Terminal, falling back to cmd) at
    /// its worktree cwd, launching the engine interactively and resuming the same conversation when the
    /// session already started (EngineSessionId set). An escape hatch: when a session is stuck at
    /// "starting engine" the user can see/continue it directly. All engines (was agy-only).</summary>
    private void OpenInTerminal(SessionViewModel? s)
    {
        if (s is null) return;
        var cwd = !string.IsNullOrWhiteSpace(s.WorktreePath) && Directory.Exists(s.WorktreePath)
            ? s.WorktreePath! : s.ProjectPath;
        if (!Directory.Exists(cwd))
        {
            s.Transcript.Add(new WorkingBlock(L("L.OpenInTerminalNoCwd")));
            return;
        }
        if (ResolveTerminalLaunch(s, cwd) is not { } launch)
        {
            s.Transcript.Add(new ErrorBlock(L("L.OpenInTerminalFailed"), L("L.EngineUnavailableBody", s.AgentName)));
            return;
        }

        try
        {
            // Windows Terminal 우선: "wt -d cwd -- <exe> <args>"; 없으면 "cmd /k <exe> <args>".
            if (TryResolveOnPath("wt.exe") is { } wt)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wt,
                    Arguments = $"-d {Quote(cwd)} -- {Quote(launch.Exe)} {launch.Args}",
                    UseShellExecute = false,
                    WorkingDirectory = cwd,
                });
                s.Transcript.Add(new WorkingBlock(L("L.OpenInTerminalLaunchedWt", s.AgentName)));
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {Quote(launch.Exe)} {launch.Args}",
                UseShellExecute = false,
                WorkingDirectory = cwd,
            });
            s.Transcript.Add(new WorkingBlock(L("L.OpenInTerminalLaunchedCmd", s.AgentName)));
        }
        catch (Exception ex)
        {
            s.Transcript.Add(new ErrorBlock(L("L.OpenInTerminalFailed"), ex.Message));
        }
    }

    /// <summary>Per-engine INTERACTIVE launch (exe + args) for the terminal escape hatch, resuming the
    /// engine's own conversation when known. Interactive forms differ from the stdio adapters: cc drops
    /// stream-json (<c>--resume id</c>), gx is the TUI <c>resume id</c> (not <c>exec resume</c>), pi drops
    /// <c>--mode rpc</c> and runs via node, agy uses its cwd→conversation cache. Null = engine unresolved.</summary>
    private (string Exe, string Args)? ResolveTerminalLaunch(SessionViewModel s, string cwd)
    {
        var id = s.EngineSessionId;
        var exe = EngineRegistry.ResolveExe(s.AgentId, _claudePath, _codexPath, _agyPath, _piPath);
        return s.AgentId switch
        {
            "cc" => exe is null ? null : (exe, string.IsNullOrEmpty(id) ? "" : $"--resume {Quote(id!)}"),
            "gx" => exe is null ? null : (exe, string.IsNullOrEmpty(id) ? "" : $"resume {Quote(id!)}"),
            // pi is a node script (exe = dist/cli.js); interactive = node <cli.js> [--session id] (no --mode rpc).
            "pi" => exe is null ? null : ("node", string.IsNullOrEmpty(id) ? Quote(exe) : $"{Quote(exe)} --session {Quote(id!)}"),
            "agy" => (exe ?? "agy",
                      (AgyAdapter.FindConversationId(cwd) is { Length: > 0 } c ? $"--conversation {Quote(c)}" : "--continue")
                      + " --dangerously-skip-permissions"),
            _ => null,
        };
    }

    /// <summary>PATH에서 bare 실행파일(wt.exe 등)의 전체 경로를 찾는다. 없으면 null.</summary>
    private static string? TryResolveOnPath(string name) => CoreHelpers.TryResolveOnPath(name);

    /// <summary>Windows 인자용 quoting (공백 포함 시 큰따옴표, 내부 따옴표는 백슬래시 이스케이프).</summary>
    private static string Quote(string s) => CoreHelpers.Quote(s);

    private static string? FindVsCodeCli() => CoreHelpers.FindVsCodeCli();

    /// <summary>Attention signal for the View (taskbar flash/sound when the window is unfocused).
    /// Reasons: "approval" (input needed now), "done", "error".</summary>
    public event Action<string, SessionViewModel>? AttentionRequested;

    // ----- approval broker (Stage 1: Claude) -----
    // The decision round-trip is owned by the Core ApprovalBroker (so a headless core/CLI can answer);
    // the VM keeps only the UI mapping requestId → (block, session) for rendering + expiry. Step 5.
    private readonly AgentManager.Core.Orchestration.ApprovalBroker _broker = new();
    private readonly Dictionary<string, (ApprovalBlock Block, SessionViewModel Session)> _approvalUi = [];
    public RelayCommand ApproveCommand { get; }
    public RelayCommand ApproveForSessionCommand { get; }
    public RelayCommand DenyCommand { get; }

    private Task<PermissionDecision> HandlePermissionAsync(SessionViewModel s, PermissionRequest pr)
    {
        // Register + render on the UI thread (the engine calls this from its own thread); Invoke<T> hands
        // the awaited task back. The broker owns the completion; the VM owns the block/session mapping.
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var task = _broker.Request(pr.RequestId);
            var summary = pr.InputJson.Length > 400 ? pr.InputJson[..400] + "…" : pr.InputJson;
            var block = new ApprovalBlock(pr.RequestId, pr.ToolName, summary)
            {
                SupportsSessionApproval = s.IsCodex, // codex app-server만 acceptForSession 지원
            };
            s.Transcript.Add(block);
            _approvalUi[pr.RequestId] = (block, s);
            s.Status = "waiting";
            s.Activity = L("L.WaitingApproval", pr.ToolName);
            AttentionRequested?.Invoke("approval", s);
            return task;
        });
    }

    public void ResolveApproval(string requestId, bool allow, bool forSession = false)
    {
        if (!_approvalUi.Remove(requestId, out var entry)) return;
        entry.Block.State = allow ? (forSession ? "allowed (session)" : "allowed") : "denied";
        entry.Session.Status = "running";
        entry.Session.Activity = allow ? L("L.ApprovalGrantedContinue", entry.Block.ToolName) : L("L.ApprovalRejected", entry.Block.ToolName);
        _broker.Resolve(requestId, new PermissionDecision(allow, allow ? null : L("L.PermissionDenied"), forSession));
        SaveState();
    }

    /// <summary>턴 종료/중지 시 남은 승인 요청은 거부로 정리(엔진은 이미 종료됨).</summary>
    private void ExpirePendingApprovals(SessionViewModel s)
    {
        foreach (var key in _approvalUi.Where(kv => ReferenceEquals(kv.Value.Session, s)).Select(kv => kv.Key).ToList())
        {
            if (_approvalUi.Remove(key, out var entry))
            {
                entry.Block.State = "expired";
                _broker.Resolve(key, new PermissionDecision(false, L("L.SessionEnded")));
            }
        }
    }

    // running sessions → their cancellation source (Stop). Owned by the Core RunRegistry so turn-loop
    // ownership can move to Core (a closed window / CLI drives the same registry). Overhaul (a) step 5.
    private readonly AgentManager.Core.Orchestration.RunRegistry _runs = new();

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
    public RelayCommand OpenProjectFolderCommand { get; }
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
    public int MaxConcurrentSessions
    {
        get => _settings.MaxConcurrentSessions;
        set { var v = Math.Max(1, value); if (_settings.MaxConcurrentSessions != v) { _settings.MaxConcurrentSessions = v; OnChanged(nameof(MaxConcurrentSessions)); } }
    }

    /// <summary>워커 전용 동시 실행 cap (메인 cap과 분리, 설정·영속).</summary>
    public int MaxConcurrentWorkers
    {
        get => _settings.MaxConcurrentWorkers;
        set { var v = Math.Max(1, value); if (_settings.MaxConcurrentWorkers != v) { _settings.MaxConcurrentWorkers = v; OnChanged(nameof(MaxConcurrentWorkers)); } }
    }

    /// <summary>새 워커 기본 행동 규칙 preamble 템플릿 (설정·영속). 빈값이면 기본 템플릿 사용.</summary>
    public string WorkerBehaviorPreamble
    {
        get => _settings.WorkerBehaviorPreamble;
        set { if (_settings.WorkerBehaviorPreamble != value) { _settings.WorkerBehaviorPreamble = value; OnChanged(nameof(WorkerBehaviorPreamble)); } }
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

    /// <summary>세션마다 고유한 브랜치명 — 같은 제목의 세션 둘이어도 동일 "agent/&lt;slug&gt;" 브랜치로
    /// `git worktree add`가 "already checked out" 실패하지 않게 세션 id의 seq 접미사를 붙인다
    /// (worktree 디렉토리는 이미 id 기반이라 따로 안 겹친다). worker/ 위임·예약 실행도 공유.</summary>
    private static string UniqueBranch(string baseName, string id)
        => baseName + "-" + id[(id.LastIndexOf('-') + 1)..];

    /// <summary>Fork: 트랜스크립트·엔진세션id를 상속한 새 세션(새 worktree). 다음 턴은 같은 대화에서 분기.</summary>
    public void ForkSession(SessionViewModel? src)
    {
        if (src is null) return;
        var engine = EngineRegistry.Get(src.AgentId);
        var title = src.Title + " (fork)";
        var id = NewSessionId("s");
        var s = new SessionViewModel(id, engine, title, UniqueBranch("agent/" + Slug(title), id),
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
        _runs.Cancel(s.Id);
        if (s.WorktreePath is not null)
        {
            await GitWorktree.RemoveAsync(s.ProjectPath, s.WorktreePath);
            s.WorktreePath = null;
            // The worktree is gone but its agent branch lingers — that residue is what used to
            // accumulate. Safe-delete it (merged only); a branch with unmerged commits is kept.
            await GitWorktree.RemoveBranchAsync(s.ProjectPath, s.Branch);
        }
        s.PropertyChanged -= SessionStatusWatch;
        _allSessions.Remove(s);
        // Worker gone → release its tasks (pending back to backlog, finished dropped) so no ghost queue lingers.
        if (s.IsWorker) _taskStore.RemoveWorker(s.Id);
        // 가져온 CLI 세션을 지우면 재발견으로 되살아나지 않게 dismiss로 기록.
        if (!string.IsNullOrEmpty(s.EngineSessionId)) _dismissedCliSessions.Add(s.EngineSessionId!);
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
            _runs.Cancel(s.Id);
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
        if (ActiveSession is { } s)
            _runs.Cancel(s.Id);
    }

    public string PersistencePath => AppStateStore.StatePath;

    public bool IsReviewOpen
    {
        get => _settings.ReviewPaneOpen;
        set { if (_settings.ReviewPaneOpen != value) { _settings.ReviewPaneOpen = value; OnChanged(nameof(IsReviewOpen)); OnChanged(nameof(ReviewPaneWidth)); SaveState(); } }
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
                RebuildTaskReports();  // "보고" 탭: 활성 세션의 워커 작업 보고 피드
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
                RebuildTaskViews();   // 백로그/워커 큐 뷰를 활성 프로젝트로 필터
                _ = LoadCliHistoryAsync(value);
                SaveState();
            }
        }
    }

    // ----- new-agent overlay state -----
    private bool _showNew;
    public bool ShowNewAgent { get => _showNew; set { if (Set(ref _showNew, value) && value) { OnChanged(nameof(NewAgentEngineOptions)); NewAgentTranslation = TranslationEnabled; NewAgentIsolate = true; NewAgentAsWorker = false; } } }

    /// <summary>New Agent 엔진 피커용 — 각 엔진 + 설치 여부(수동 경로/PATH 반영). 폼 열 때 새로 계산.</summary>
    public IReadOnlyList<EngineOptionVm> NewAgentEngineOptions =>
        Engines.Select(d => new EngineOptionVm(d,
            EngineRegistry.IsInstalled(d.Id, _claudePath, _codexPath, _agyPath, _piPath),
            IsEngineLimited(d.Id),
            WillUseApiOnLimit(d.Id))).ToList();

    // ----- 설치 & 세팅 가이드 모달 (Resources/Guide.<lang>.md를 MarkdownViewer로 렌더) -----
    private bool _showInstallGuide;
    public bool ShowInstallGuide
    {
        get => _showInstallGuide;
        set { if (!Set(ref _showInstallGuide, value)) return; if (value) InstallGuideMarkdown = LoadGuide(_language); OnChanged(nameof(IsModalActive)); }
    }
    private string _installGuideMarkdown = "";
    public string InstallGuideMarkdown { get => _installGuideMarkdown; private set => Set(ref _installGuideMarkdown, value); }

    private static string LoadGuide(string lang)
    {
        var file = lang == "en" ? "Resources/Guide.en.md" : "Resources/Guide.ko.md";
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(file, UriKind.Relative));
            if (info is null) return "";
            using var reader = new System.IO.StreamReader(info.Stream);
            return reader.ReadToEnd();
        }
        catch { return ""; }
    }
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
                if (value?.Id == "pi") _ = QueryPiModelsAsync();   // pi 모델 목록 동적 조회(드롭다운 채움)
                // 추론 수준 옵션/기본값 엔진별 재계산 (cc/gx/pi 노출, agy 제외)
                OnChanged(nameof(NewAgentHasEffort));
                OnChanged(nameof(NewAgentEffortOptions));
                NewAgentReasoning = SessionViewModel.RecommendedEffort(value?.Id, NewAgentModel);
            }
        }
    }
    public string[] NewAgentModels => _newEngine is { } e ? DropdownModelsFor(e.Id) : [];
    private string _newAgentModel = "";
    // 모델을 바꾸면 그 모델의 권장 effort로 기본값을 맞춘다(예: opus → medium). 게이팅이 아니라 스마트 기본값 — 이후 변경 가능.
    public string NewAgentModel { get => _newAgentModel; set { if (Set(ref _newAgentModel, value)) { OnChanged(nameof(NewAgentEffortOptions)); NewAgentReasoning = SessionViewModel.RecommendedEffort(_newEngine?.Id, value); } } }

    /// <summary>추론 수준을 소비하는 엔진(cc: --effort, gx: model_reasoning_effort, pi: --thinking)만 노출.
    /// agy만 추론 플래그가 없음. SessionViewModel.HasEffort(= not agy)와 일치.</summary>
    public bool NewAgentHasEffort => _newEngine is { } e && (_engineConfig?.Get(e.Id) is { } c ? c.HasEfforts : _modelCatalog.HasEfforts(e.Id));
    /// <summary>New-Agent 추론 단계 — 엔진별 config의 모델별 effort(없으면 엔진 기본; 미로드 시 카탈로그 폴백).</summary>
    public string[] NewAgentEffortOptions => _newEngine is { } e
        ? (_engineConfig?.Get(e.Id) is { } c ? [.. c.EffortsFor(NewAgentModel)] : [.. _modelCatalog.EffortsFor(e.Id, NewAgentModel)])
        : [];
    private string _newAgentReasoning = "default";
    public string NewAgentReasoning { get => _newAgentReasoning; set => Set(ref _newAgentReasoning, value); }

    /// <summary>worktree 격리 여부(기본 ON). 끄면 프로젝트 루트 공유(WorktreeOptOut).</summary>
    private bool _newAgentIsolate = true;
    public bool NewAgentIsolate
    {
        get => _newAgentIsolate;
        set { if (Set(ref _newAgentIsolate, value)) { OnChanged(nameof(NewAgentBranchPreview)); OnChanged(nameof(NewAgentNoWorktree)); } }
    }
    /// <summary>"워크트리 미사용" 체크박스 — NewAgentIsolate의 반전(체크 = 워크트리 안 만들고 메인 트리 공유).</summary>
    public bool NewAgentNoWorktree { get => !_newAgentIsolate; set => NewAgentIsolate = !value; }
    /// <summary>true면 일반 세션 대신 워커(작업 대기, 자동 실행 안 함)로 생성.</summary>
    private bool _newAgentAsWorker;
    public bool NewAgentAsWorker
    {
        get => _newAgentAsWorker;
        set { if (Set(ref _newAgentAsWorker, value)) OnChanged(nameof(NewAgentBranchPreview)); }
    }
    /// <summary>New Agent 폼의 번역 선택(생성 시 세션에 고정). 폼 열 때 전역값+Ollama 가용성으로 초기화.</summary>
    private bool _newAgentTranslation = true;
    public bool NewAgentTranslation
    {
        get => _newAgentTranslation;
        set
        {
            if (value && !OllamaRunning) value = false; // Ollama 꺼짐이면 켤 수 없음 — OFF 유지(⚠ 안내)
            if (Set(ref _newAgentTranslation, value)) OnChanged(nameof(NewAgentTranslationLabel));
        }
    }
    public string NewAgentTranslationLabel => _newAgentTranslation ? L("L.TranslationOn") : L("L.TranslationOff");
    /// <summary>생성될 worktree 브랜치 미리보기 — 격리 OFF면 공유 안내, 워커면 worker/ 접두.</summary>
    public string NewAgentBranchPreview =>
        !_newAgentIsolate ? L("L.WorktreeShared")
        : (_newAgentAsWorker ? "worker/" : "agent/") + Slug(string.IsNullOrWhiteSpace(_newTitle) ? "task" : _newTitle);
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
        // 모델은 자유 입력(편집 가능 콤보): 별칭(sonnet)·풀네임·새 모델 전부 그대로 통과 — 정적 목록에
        // 가두지 않는다(새 모델마다 패치 불필요). 잘못된 값이면 엔진이 런타임에 에러로 알려준다.
        var model = !string.IsNullOrWhiteSpace(NewAgentModel)
            ? NewAgentModel.Trim()
            : (DefaultModelFor(engine.Id) is { Length: > 0 } dm ? dm : engine.Models[0]);

        // 워커로 생성: 작업 대기 풀에 추가만 하고 자동 실행하지 않음(작업 할당 시 구동).
        if (NewAgentAsWorker)
        {
            var w = CreateWorkerSession(engine, model, project,
                string.IsNullOrWhiteSpace(NewAgentTitle) ? null : NewAgentTitle.Trim(),
                NewAgentTranslation, "", "", null);
            if (NewAgentHasEffort && !string.IsNullOrWhiteSpace(NewAgentReasoning)) w.ReasoningEffort = NewAgentReasoning;
            w.WorktreeOptOut = !NewAgentIsolate;
            ActiveSession = w;
            ShowNewAgent = false;
            NewAgentTitle = "";
            SaveState();
            return;
        }

        var title = string.IsNullOrWhiteSpace(NewAgentTitle) ? $"New {engine.Name} session" : NewAgentTitle.Trim();
        var id = NewSessionId("s");
        var branch = UniqueBranch("agent/" + Slug(title), id);
        var (reqAppr, sandbox) = PolicyToSession(_approvalPolicy);
        var s = new SessionViewModel(id, engine, title, branch, project.Id, project.Name, project.Path, model)
        {
            TranslationEnabled = NewAgentTranslation,
            RequireApproval = reqAppr,
            Sandbox = sandbox,
            WorktreeOptOut = !NewAgentIsolate,
        };
        if (NewAgentHasEffort && !string.IsNullOrWhiteSpace(NewAgentReasoning)) s.ReasoningEffort = NewAgentReasoning;
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
        // 번역 토글이 바뀌면 떠 있는 구조화 선택지의 질문·옵션 표시를 다시 번역/원복.
        if (e.PropertyName == nameof(SessionViewModel.TranslationEnabled) && sender is SessionViewModel ts && ts.ActiveChoice is { } flow)
            _ = ApplyChoiceTranslationAsync(ts, flow);

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
            if (sender is SessionViewModel ses)
            {
                // Ollama 꺼짐이면 번역을 켤 수 없음 — 다시 OFF로 되돌린다(⚠ 아이콘이 사유 안내).
                if (ses.TranslationEnabled && !OllamaRunning) { ses.TranslationEnabled = false; return; }
                // 토글을 켜면 아직 원문(영어)인 기존 블록도 번역한다(재동기화·임포트·번역 OFF로 받은 과거 turn 포함).
                if (ses.TranslationEnabled) _ = TranslatePendingBlocksAsync(ses);
            }
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
