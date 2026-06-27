using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AgentManager.Core.Workers;

namespace AgentManager.ViewModels;

/// <summary>Display wrapper for one worker task, rebuilt from the Core store's DTO on every change
/// (the store is the source of truth — this holds no mutable domain state).</summary>
public sealed class WorkerTaskViewModel : ObservableObject
{
    public string Id { get; }
    public string Title { get; }
    public string Prompt { get; }
    public string Engine { get; }
    public string Status { get; }
    public string AssignedWorkerId { get; }
    public string AssignedWorkerName { get; }
    public int Order { get; }
    public string Report { get; }

    public WorkerTaskViewModel(WorkerTaskDto dto, string assignedWorkerName = "")
    {
        Id = dto.Id; Title = dto.Title; Prompt = dto.Prompt; Engine = dto.Engine;
        Status = string.IsNullOrWhiteSpace(dto.Status) ? WorkerTaskStatus.Backlog : dto.Status;
        AssignedWorkerId = dto.AssignedWorkerId ?? "";
        AssignedWorkerName = assignedWorkerName;
        Order = dto.Order;
        Report = dto.Report ?? "";
    }

    public string ReportPreview => Report.Length <= 600 ? Report : Report[..599] + "…";
    public string PromptPreview => Prompt.Length <= 200 ? Prompt : Prompt[..199] + "…";

    /// <summary>Report-inbox multi-select state (UI-only; copy actions read it, no domain meaning).</summary>
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    /// <summary>Plain-text form placed on the clipboard: title header + the full report body.</summary>
    public string ClipboardText => string.IsNullOrWhiteSpace(Title) ? Report : $"## {Title}\n\n{Report}";
    public string EngineLabel => string.IsNullOrEmpty(Engine) ? "" : Engine.ToUpperInvariant();
    public bool HasEngine => !string.IsNullOrEmpty(Engine);
    public bool IsRunning => Status == WorkerTaskStatus.Running;
    public bool IsFinished => WorkerTaskStatus.IsFinished(Status);
    /// <summary>A single task can be run when queued (assigned) or retried (failed).</summary>
    public bool CanRun => Status is WorkerTaskStatus.Assigned or WorkerTaskStatus.Failed;

    public string StatusLabel => Status switch
    {
        WorkerTaskStatus.Assigned => App.L("L.TaskAssigned"),
        WorkerTaskStatus.Running => App.L("L.TaskRunning"),
        WorkerTaskStatus.Done => App.L("L.TaskDone"),
        WorkerTaskStatus.Failed => App.L("L.TaskFailed"),
        _ => App.L("L.TaskBacklog"),
    };
}

/// <summary>One worker's task queue (the per-worker list) — active tasks shown; finished tasks live
/// under a collapsible history so the queue stays focused on what's pending/running.</summary>
public sealed class WorkerQueueViewModel : ObservableObject
{
    public string WorkerId { get; }
    public string WorkerName { get; }
    public string EngineLabel { get; }
    /// <summary>Active tasks only (assigned + running) — what the queue shows by default.</summary>
    public ObservableCollection<WorkerTaskViewModel> Tasks { get; } = [];
    /// <summary>Finished tasks (done/failed) — revealed via the history toggle.</summary>
    public ObservableCollection<WorkerTaskViewModel> FinishedTasks { get; } = [];

    public WorkerQueueViewModel(string workerId, string workerName, string engineLabel,
        IEnumerable<WorkerTaskViewModel> active, IEnumerable<WorkerTaskViewModel> finished, bool showHistory)
    {
        WorkerId = workerId; WorkerName = workerName; EngineLabel = engineLabel; ShowHistory = showHistory;
        foreach (var t in active) Tasks.Add(t);
        foreach (var t in finished) FinishedTasks.Add(t);
    }

    public int PendingCount => Tasks.Count;
    public bool CanRunQueue => Tasks.Any(t => t.Status == WorkerTaskStatus.Assigned);
    public int FinishedCount => FinishedTasks.Count;
    public bool HasFinished => FinishedTasks.Count > 0;
    public bool ShowHistory { get; }
    public string CountLabel => PendingCount.ToString();
    public bool HasEngine => !string.IsNullOrEmpty(EngineLabel);
}

public sealed partial class AppViewModel
{
    /// <summary>Core domain owner: backlog + per-worker queues, ingest, lifecycle. The VM only
    /// observes it (rebuild on <see cref="WorkerTaskStore.Changed"/>) and drives execution.</summary>
    private readonly WorkerTaskStore _taskStore = new();

    /// <summary>Active-project backlog (unassigned) — the review list.</summary>
    public ObservableCollection<WorkerTaskViewModel> BacklogTasks { get; } = [];
    /// <summary>Active-project per-worker queues — each worker's own ordered task list.</summary>
    public ObservableCollection<WorkerQueueViewModel> WorkerQueues { get; } = [];
    /// <summary>Completed-task reports addressed to the active session — the side "reports" tab feed.</summary>
    public ObservableCollection<WorkerTaskViewModel> ActiveTaskReports { get; } = [];
    public bool HasActiveTaskReports => ActiveTaskReports.Count > 0;
    /// <summary>How many report cards are checked — drives the "선택 복사 (N)" button label/visibility.</summary>
    public int SelectedReportCount => ActiveTaskReports.Count(r => r.IsSelected);
    public bool HasSelectedReports => SelectedReportCount > 0;
    /// <summary>Every report card checked — drives the select-all toggle's checked state + label.</summary>
    public bool AllReportsSelected => ActiveTaskReports.Count > 0 && ActiveTaskReports.All(r => r.IsSelected);

    /// <summary>Rebuild the active session's report feed (call on session change + store change).</summary>
    public void RebuildTaskReports()
    {
        ActiveTaskReports.Clear();
        if (ActiveSession is { } a)
            foreach (var d in _taskStore.ReportsForOrigin(a.Id))
            {
                var vm = new WorkerTaskViewModel(d);
                vm.PropertyChanged += OnReportSelectionChanged;
                ActiveTaskReports.Add(vm);
            }
        OnChanged(nameof(HasActiveTaskReports));
        OnChanged(nameof(SelectedReportCount));
        OnChanged(nameof(HasSelectedReports));
        OnChanged(nameof(AllReportsSelected));
    }

    private void OnReportSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkerTaskViewModel.IsSelected)) return;
        OnChanged(nameof(SelectedReportCount));
        OnChanged(nameof(HasSelectedReports));
        OnChanged(nameof(AllReportsSelected));
    }

    private static void CopyToClipboard(string text)
    {
        try { System.Windows.Clipboard.SetText(text ?? ""); } catch { /* clipboard busy — ignore */ }
    }

    public bool HasBacklog => BacklogTasks.Count > 0;
    public bool HasWorkerQueues => WorkerQueues.Count > 0;
    public bool HasWorkerTasks => HasBacklog || HasWorkerQueues;
    public int BacklogCount => BacklogTasks.Count;

    private FileSystemWatcher? _taskSpoolWatcher;
    private readonly HashSet<string> _drivingWorkers = [];

    /// <summary>Add AGENTMANAGER_TASK_SPOOL (this project's spool dir) to the engine env, so the
    /// worker-prompt skill knows where to drop task files.</summary>
    private static IReadOnlyDictionary<string, string> WithTaskSpoolEnv(IReadOnlyDictionary<string, string> baseEnv, string projectId)
    {
        var dir = TaskSpool.DirFor(projectId);
        try { Directory.CreateDirectory(dir); } catch { }
        return new Dictionary<string, string>(baseEnv) { ["AGENTMANAGER_TASK_SPOOL"] = dir };
    }

    public RelayCommand AssignTaskCommand { get; private set; } = null!;
    public RelayCommand AssignToWorkerCommand { get; private set; } = null!;
    public RelayCommand AssignToNewWorkerCommand { get; private set; } = null!;
    public RelayCommand UnassignTaskCommand { get; private set; } = null!;
    public RelayCommand DeleteTaskCommand { get; private set; } = null!;
    public RelayCommand RunTaskCommand { get; private set; } = null!;
    public RelayCommand RunQueueCommand { get; private set; } = null!;
    public RelayCommand MoveTaskUpCommand { get; private set; } = null!;
    public RelayCommand MoveTaskDownCommand { get; private set; } = null!;
    public RelayCommand ClearFinishedCommand { get; private set; } = null!;
    public RelayCommand ToggleQueueHistoryCommand { get; private set; } = null!;
    public RelayCommand CopyReportCommand { get; private set; } = null!;
    public RelayCommand ToggleSelectAllReportsCommand { get; private set; } = null!;
    public RelayCommand CopySelectedReportsCommand { get; private set; } = null!;

    /// <summary>Per-worker: whether the finished-task history is expanded (survives view rebuilds).</summary>
    private readonly HashSet<string> _expandedQueueHistory = [];

    private WorkerTaskViewModel? _pendingAssign;
    public WorkerTaskViewModel? PendingAssign { get => _pendingAssign; private set => Set(ref _pendingAssign, value); }
    private bool _showAssignPicker;
    public bool ShowAssignPicker { get => _showAssignPicker; set => Set(ref _showAssignPicker, value); }

    private void InitWorkerTaskCommands()
    {
        _taskStore.Changed += OnTaskStoreChanged;
        _taskStore.ReconcileInterrupted(); // crashed-mid-run tasks (running) → re-queue as assigned

        // 할당 버튼 → 워커 선택 팝업 열기 (테마된 워커 목록).
        AssignTaskCommand = new RelayCommand(p =>
        {
            if (p is WorkerTaskViewModel t) { PendingAssign = t; RefreshWorkerPool(); ShowAssignPicker = true; }
        });
        // 팝업에서 워커 클릭 → 대기 중인 작업을 그 워커 큐에 추가.
        AssignToWorkerCommand = new RelayCommand(p =>
        {
            if (p is SessionViewModel w && PendingAssign is { } t)
            {
                _taskStore.Assign(t.Id, w.Id);
                ShowAssignPicker = false;
                PendingAssign = null;
            }
        });
        // "+ 새 워커": create an IDLE worker (no dummy creation turn), then assign the pending task
        // as its first — clean — turn. Keeps engine-session resume for later queue tasks (warm context,
        // no project re-scan) without inheriting a polluting creation turn.
        AssignToNewWorkerCommand = new RelayCommand(p =>
        {
            if (p is string engineId && PendingAssign is { } t && ActiveProject is { } proj)
            {
                var eng = EngineRegistry.Get(engineId);
                var model = DefaultModelFor(eng.Id) is { Length: > 0 } dm ? dm : (eng.Models.Length > 0 ? eng.Models[0] : "");
                var w = CreateWorkerSession(eng, model, proj, $"{eng.Name} worker",
                    translationEnabled: false, "Korean", "English", WorkerBehaviorPreamble);
                RefreshWorkerPool();
                _taskStore.Assign(t.Id, w.Id);
                ShowAssignPicker = false;
                PendingAssign = null;
            }
        });
        UnassignTaskCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) _taskStore.Unassign(t.Id); });
        DeleteTaskCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) _taskStore.Delete(t.Id); });
        MoveTaskUpCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) _taskStore.Move(t.Id, -1); });
        MoveTaskDownCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) _taskStore.Move(t.Id, +1); });
        ClearFinishedCommand = new RelayCommand(p => { if (p is WorkerQueueViewModel q) _taskStore.ClearFinished(q.WorkerId); });
        ToggleQueueHistoryCommand = new RelayCommand(p =>
        {
            if (p is WorkerQueueViewModel q)
            {
                if (!_expandedQueueHistory.Remove(q.WorkerId)) _expandedQueueHistory.Add(q.WorkerId);
                RebuildTaskViews();
            }
        });

        RunTaskCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) _ = RunOneAsync(t.Id); },
            p => p is WorkerTaskViewModel { CanRun: true });
        RunQueueCommand = new RelayCommand(p => { if (p is WorkerQueueViewModel q) _ = RunQueueAsync(q.WorkerId); },
            p => p is WorkerQueueViewModel { CanRunQueue: true });

        // Report inbox: single-card copy (per card) + select-all toggle + copy the checked subset.
        // Copy is a manual hand-off to the session (clipboard, not draft injection).
        CopyReportCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) CopyToClipboard(t.ClipboardText); });
        ToggleSelectAllReportsCommand = new RelayCommand(_ =>
        {
            var selectAll = !AllReportsSelected;   // none/partial → select all; all → clear
            foreach (var r in ActiveTaskReports) r.IsSelected = selectAll;
        });
        CopySelectedReportsCommand = new RelayCommand(_ =>
        {
            var picked = ActiveTaskReports.Where(r => r.IsSelected).ToList();
            if (picked.Count == 0) return;
            CopyToClipboard(string.Join("\n\n---\n\n", picked.Select(r => r.ClipboardText)));
        });
    }

    private void OnTaskStoreChanged() { RebuildTaskViews(); SaveState(); }

    // ---- execution (drives the Core queue through the turn pipeline) -----

    /// <summary>Run a single task now (retrying a failed one by re-queuing it first).</summary>
    private Task RunOneAsync(string taskId)
    {
        var t = _taskStore.Find(taskId);
        if (t is null) return Task.CompletedTask;
        if (t.Status == WorkerTaskStatus.Failed) _taskStore.SetStatus(taskId, WorkerTaskStatus.Assigned);
        return DriveWorkerAsync(t.AssignedWorkerId, taskId);
    }

    /// <summary>Run a worker's whole queue sequentially, auto-advancing to the next task.</summary>
    private Task RunQueueAsync(string workerId) => DriveWorkerAsync(workerId, null);

    /// <summary>Core loop: pull the next runnable task from the store, run it on the worker via the
    /// shared turn pipeline, record the result, and (for a full queue run) advance. One driver per
    /// worker at a time; stops the queue on a failure.</summary>
    private async Task DriveWorkerAsync(string workerId, string? onlyTaskId)
    {
        if (string.IsNullOrEmpty(workerId) || !_drivingWorkers.Add(workerId)) return;
        try
        {
            var worker = _allSessions.FirstOrDefault(s => s.Id == workerId);
            if (worker is null)
            {
                // assigned worker no longer exists → return its queue to the backlog
                foreach (var d in _taskStore.QueueFor(workerId).ToList()) _taskStore.Unassign(d.Id);
                return;
            }
            while (!IsWorkerBusy(worker))
            {
                var next = onlyTaskId is null
                    ? _taskStore.NextRunnable(workerId)
                    : (_taskStore.Find(onlyTaskId) is { Status: WorkerTaskStatus.Assigned } single ? single : null);
                if (next is null) break;

                // Respect the global worker cap: RunTurnAsync rejects (does not wait) once the cap is
                // reached, which would falsely mark this task failed. Leave it assigned to run later.
                if (_allSessions.Count(x => x.IsWorker && _running.ContainsKey(x.Id)) >= MaxConcurrentWorkers) break;

                _taskStore.SetStatus(next.Id, WorkerTaskStatus.Running);
                var before = worker.Transcript.OfType<AgentTextBlock>().Count();
                var statusBefore = worker.Status;
                var stop = false;
                try
                {
                    await RunTurnAsync(worker, WorkerDefaults.ComposePrompt(worker.BehaviorPreamble, next.Prompt));
                    var fresh = worker.Transcript.OfType<AgentTextBlock>().Skip(before).ToList();
                    var produced = fresh.Count > 0;
                    // only record if the user hasn't unassigned/deleted it mid-run
                    if (_taskStore.Find(next.Id)?.Status == WorkerTaskStatus.Running)
                    {
                        if (!produced && worker.Status == statusBefore)
                        {
                            // the turn never actually ran (e.g. a residual cap rejection) — keep it
                            // queued so it runs when a worker slot frees, instead of failing it.
                            _taskStore.SetStatus(next.Id, WorkerTaskStatus.Assigned);
                            stop = true;
                        }
                        else if (worker.Status == "done" && produced)
                        {
                            // capture the worker's final reply as this task's report (back to the origin session)
                            var last = fresh[^1];
                            _taskStore.SetReport(next.Id, last.OriginalText ?? last.Text ?? "");
                            _taskStore.SetStatus(next.Id, WorkerTaskStatus.Done);
                        }
                        else
                            _taskStore.SetStatus(next.Id, WorkerTaskStatus.Failed);
                    }
                }
                catch
                {
                    if (_taskStore.Find(next.Id)?.Status == WorkerTaskStatus.Running)
                        _taskStore.SetStatus(next.Id, WorkerTaskStatus.Failed);
                }

                if (stop) break;                                                    // couldn't run now — retry later
                if (onlyTaskId is not null) break;                                  // single-task run
                if (_taskStore.Find(next.Id)?.Status == WorkerTaskStatus.Failed) break; // stop queue on failure
            }
        }
        finally { _drivingWorkers.Remove(workerId); }
    }

    // ---- view projection ------------------------------------------------

    /// <summary>Rebuild the active-project backlog + per-worker queue views from the store.</summary>
    public void RebuildTaskViews()
    {
        var pid = ActiveProject?.Id;

        BacklogTasks.Clear();
        foreach (var d in _taskStore.Backlog(pid)) BacklogTasks.Add(new WorkerTaskViewModel(d));

        WorkerQueues.Clear();
        foreach (var wid in _taskStore.WorkerIdsWithTasks(pid))
        {
            var w = _allSessions.FirstOrDefault(s => s.Id == wid);
            var name = w?.Title ?? "worker";
            var engine = w is null ? "" : w.AgentId.ToUpperInvariant();
            var all = _taskStore.AssignedTo(wid).Select(d => new WorkerTaskViewModel(d, name)).ToList();
            var active = all.Where(t => !t.IsFinished);   // assigned + running — shown in the queue
            var finished = all.Where(t => t.IsFinished);  // done/failed — under the history toggle
            WorkerQueues.Add(new WorkerQueueViewModel(wid, name, engine, active, finished, _expandedQueueHistory.Contains(wid)));
        }

        OnChanged(nameof(HasBacklog));
        OnChanged(nameof(HasWorkerQueues));
        OnChanged(nameof(HasWorkerTasks));
        OnChanged(nameof(BacklogCount));
        RebuildTaskReports();
    }

    private void LoadWorkerTasks(IEnumerable<WorkerTaskDto> dtos)
    {
        _taskStore.Load(dtos);
        RebuildTaskViews(); // explicit: during restore the Changed subscription isn't attached yet
    }

    /// <summary>Tasks to persist (Core store is the source of truth).</summary>
    private IReadOnlyList<WorkerTaskDto> WorkerTasksSnapshot() => _taskStore.Snapshot();

    // ---- spool watcher --------------------------------------------------

    /// <summary>Watch the task-spool root; ingest skill-written task files into the backlog.</summary>
    private void StartTaskSpoolWatcher()
    {
        try
        {
            Directory.CreateDirectory(TaskSpool.Root);
            _taskSpoolWatcher = new FileSystemWatcher(TaskSpool.Root, "*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            // Created/Changed for direct writes, Renamed for atomic temp→.json writes.
            _taskSpoolWatcher.Created += (_, e) => ScheduleIngest(e.FullPath);
            _taskSpoolWatcher.Changed += (_, e) => ScheduleIngest(e.FullPath);
            _taskSpoolWatcher.Renamed += (_, e) => ScheduleIngest(e.FullPath);
            // ingest anything already sitting in the spool from a previous run
            foreach (var f in Directory.EnumerateFiles(TaskSpool.Root, "*.json", SearchOption.AllDirectories))
                ScheduleIngest(f);
        }
        catch { /* spool optional — never block startup */ }
    }

    /// <summary>FS events can fire mid-write; ingest after a short delay on the UI thread.
    /// <paramref name="projectId"/> non-null = file came from a session's .am/worker-tasks/.</summary>
    private void ScheduleIngest(string path, string? projectId = null, string originSessionId = "") =>
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(150);
            IngestSpoolFile(path, projectId, originSessionId);
        });

    private void IngestSpoolFile(string path, string? projectId = null, string originSessionId = "")
    {
        try
        {
            if (!File.Exists(path)) return;
            var added = projectId is null ? _taskStore.IngestFile(path) : _taskStore.IngestFile(path, projectId, originSessionId);
            if (added.Count > 0) { try { File.Delete(path); } catch { } } // raises Changed → rebuild + save
            // 0 added = empty/partial write — leave the file; a later event retries
        }
        catch { }
    }

    private readonly HashSet<string> _watchedTaskDirs = [];
    private readonly System.Collections.Generic.List<FileSystemWatcher> _sessionTaskWatchers = [];

    /// <summary>Also watch a running session's <c>&lt;cwd&gt;/.am/worker-tasks/</c> — the worker-prompt
    /// skill's fallback when AGENTMANAGER_TASK_SPOOL isn't visible to the agent's shell. Ingests those
    /// files into the backlog under the session's project. Idempotent per directory.</summary>
    private void WatchSessionTaskSpool(string cwd, string projectId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return;
        try
        {
            var dir = Path.Combine(cwd, ".am", "worker-tasks");
            Directory.CreateDirectory(dir);
            if (!_watchedTaskDirs.Add(dir)) return; // already watching
            foreach (var f in Directory.EnumerateFiles(dir, "*.json")) ScheduleIngest(f, projectId, sessionId);
            var w = new FileSystemWatcher(dir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Created += (_, e) => ScheduleIngest(e.FullPath, projectId, sessionId);
            w.Changed += (_, e) => ScheduleIngest(e.FullPath, projectId, sessionId);
            w.Renamed += (_, e) => ScheduleIngest(e.FullPath, projectId, sessionId);
            _sessionTaskWatchers.Add(w); // keep alive for the app's lifetime
        }
        catch { /* best-effort — never block a turn */ }
    }
}
