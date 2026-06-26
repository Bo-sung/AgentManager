using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AgentManager.Core.Workers;

namespace AgentManager.ViewModels;

/// <summary>One worker task (backlog or assigned-to-worker). Source = the worker-prompt skill's
/// spool output; the user reviews the backlog, then assigns each to a worker.</summary>
public sealed class WorkerTaskViewModel : ObservableObject
{
    public string Id { get; }
    public string ProjectId { get; }
    public string Title { get; }
    public string Prompt { get; }
    public string Engine { get; }

    public WorkerTaskViewModel(WorkerTaskDto dto)
    {
        Id = dto.Id; ProjectId = dto.ProjectId; Title = dto.Title; Prompt = dto.Prompt; Engine = dto.Engine;
        _status = string.IsNullOrWhiteSpace(dto.Status) ? "backlog" : dto.Status;
        _assignedWorkerId = dto.AssignedWorkerId ?? "";
    }

    private string _status;
    public string Status { get => _status; set { if (Set(ref _status, value)) OnChanged(nameof(StatusLabel)); } }

    private string _assignedWorkerId;
    public string AssignedWorkerId { get => _assignedWorkerId; set => Set(ref _assignedWorkerId, value); }

    /// <summary>Worker chosen in the row's dropdown before pressing Assign (UI-only).</summary>
    private SessionViewModel? _selectedWorker;
    public SessionViewModel? SelectedWorker { get => _selectedWorker; set => Set(ref _selectedWorker, value); }

    private string _assignedWorkerName = "";
    public string AssignedWorkerName { get => _assignedWorkerName; set => Set(ref _assignedWorkerName, value); }

    public string PromptPreview => Prompt.Length <= 200 ? Prompt : Prompt[..199] + "…";
    public string EngineLabel => string.IsNullOrEmpty(Engine) ? "" : Engine.ToUpperInvariant();
    public bool HasEngine => !string.IsNullOrEmpty(Engine);

    public string StatusLabel => _status switch
    {
        "assigned" => App.L("L.TaskAssigned"),
        "running" => App.L("L.TaskRunning"),
        "done" => App.L("L.TaskDone"),
        "failed" => App.L("L.TaskFailed"),
        _ => App.L("L.TaskBacklog"),
    };

    public WorkerTaskDto ToDto() => new()
    {
        Id = Id, ProjectId = ProjectId, Title = Title, Prompt = Prompt, Engine = Engine,
        Status = _status, AssignedWorkerId = _assignedWorkerId,
    };
}

public sealed partial class AppViewModel
{
    /// <summary>All worker tasks across projects (persisted).</summary>
    public ObservableCollection<WorkerTaskViewModel> AllWorkerTasks { get; } = [];
    /// <summary>Active-project backlog (unassigned) — the review list.</summary>
    public ObservableCollection<WorkerTaskViewModel> BacklogTasks { get; } = [];
    /// <summary>Active-project tasks already assigned to a worker — the per-worker queues.</summary>
    public ObservableCollection<WorkerTaskViewModel> AssignedTasks { get; } = [];

    public bool HasBacklog => BacklogTasks.Count > 0;
    public bool HasAssignedTasks => AssignedTasks.Count > 0;
    public bool HasWorkerTasks => HasBacklog || HasAssignedTasks;

    private FileSystemWatcher? _taskSpoolWatcher;

    /// <summary>Add AGENTMANAGER_TASK_SPOOL (this project's spool dir) to the engine env, so the
    /// worker-prompt skill knows where to drop task files.</summary>
    private static System.Collections.Generic.IReadOnlyDictionary<string, string> WithTaskSpoolEnv(
        System.Collections.Generic.IReadOnlyDictionary<string, string> baseEnv, string projectId)
    {
        var dir = TaskSpool.DirFor(projectId);
        try { Directory.CreateDirectory(dir); } catch { }
        return new System.Collections.Generic.Dictionary<string, string>(baseEnv) { ["AGENTMANAGER_TASK_SPOOL"] = dir };
    }

    public RelayCommand AssignTaskCommand { get; private set; } = null!;
    public RelayCommand AssignToWorkerCommand { get; private set; } = null!;
    public RelayCommand UnassignTaskCommand { get; private set; } = null!;
    public RelayCommand DeleteTaskCommand { get; private set; } = null!;
    public RelayCommand RunTaskCommand { get; private set; } = null!;

    private WorkerTaskViewModel? _pendingAssign;
    public WorkerTaskViewModel? PendingAssign { get => _pendingAssign; private set => Set(ref _pendingAssign, value); }
    private bool _showAssignPicker;
    public bool ShowAssignPicker { get => _showAssignPicker; set => Set(ref _showAssignPicker, value); }

    private void InitWorkerTaskCommands()
    {
        // 할당 버튼 → 워커 선택 팝업 열기 (테마된 워커 목록).
        AssignTaskCommand = new RelayCommand(p =>
        {
            if (p is WorkerTaskViewModel t) { PendingAssign = t; RefreshWorkerPool(); ShowAssignPicker = true; }
        });
        // 팝업에서 워커 클릭 → 대기 중인 작업을 그 워커에 할당.
        AssignToWorkerCommand = new RelayCommand(p =>
        {
            if (p is SessionViewModel w && PendingAssign is { } t)
            {
                t.AssignedWorkerId = w.Id;
                t.AssignedWorkerName = w.Title;
                t.Status = "assigned";
                ShowAssignPicker = false;
                PendingAssign = null;
                RefreshTaskViews();
                SaveState();
            }
        });
        UnassignTaskCommand = new RelayCommand(p =>
        {
            if (p is WorkerTaskViewModel t) { t.AssignedWorkerId = ""; t.Status = "backlog"; RefreshTaskViews(); SaveState(); }
        });
        DeleteTaskCommand = new RelayCommand(p =>
        {
            if (p is WorkerTaskViewModel t) { AllWorkerTasks.Remove(t); RefreshTaskViews(); SaveState(); }
        });
        RunTaskCommand = new RelayCommand(p => { if (p is WorkerTaskViewModel t) _ = RunWorkerTaskAsync(t); },
            p => p is WorkerTaskViewModel { Status: "assigned" });
    }

    /// <summary>Run an assigned task on its worker (reuses the turn pipeline); capture the report.</summary>
    private async Task RunWorkerTaskAsync(WorkerTaskViewModel t)
    {
        var worker = _allSessions.FirstOrDefault(s => s.Id == t.AssignedWorkerId);
        if (worker is null || IsWorkerBusy(worker) || t.Status != "assigned") return;
        t.Status = "running";
        try
        {
            await RunTurnAsync(worker, WorkerDefaults.ComposePrompt(worker.BehaviorPreamble, t.Prompt));
            var last = worker.Transcript.OfType<AgentTextBlock>().LastOrDefault();
            t.Status = worker.Status == "error" || last is null ? "failed" : "done";
        }
        catch { t.Status = "failed"; }
        RefreshTaskViews();
        SaveState();
    }

    /// <summary>Rebuild the active-project backlog/assigned views from AllWorkerTasks.</summary>
    public void RefreshTaskViews()
    {
        var pid = ActiveProject?.Id;
        BacklogTasks.Clear();
        AssignedTasks.Clear();
        foreach (var t in AllWorkerTasks.Where(x => pid is null || x.ProjectId == pid))
        {
            if (string.IsNullOrEmpty(t.AssignedWorkerId) || t.Status == "backlog")
                BacklogTasks.Add(t);
            else
            {
                t.AssignedWorkerName = _allSessions.FirstOrDefault(s => s.Id == t.AssignedWorkerId)?.Title ?? "worker";
                AssignedTasks.Add(t);
            }
        }
        OnChanged(nameof(HasBacklog));
        OnChanged(nameof(HasAssignedTasks));
        OnChanged(nameof(HasWorkerTasks));
        OnChanged(nameof(BacklogCount));
    }

    public int BacklogCount => BacklogTasks.Count;

    private void LoadWorkerTasks(System.Collections.Generic.IEnumerable<WorkerTaskDto> dtos)
    {
        AllWorkerTasks.Clear();
        foreach (var d in dtos) AllWorkerTasks.Add(new WorkerTaskViewModel(d));
        RefreshTaskViews();
    }

    /// <summary>Watch the task-spool root; ingest skill-written task files into the backlog.</summary>
    private void StartTaskSpoolWatcher()
    {
        try
        {
            Directory.CreateDirectory(TaskSpool.Root);
            _taskSpoolWatcher = new FileSystemWatcher(TaskSpool.Root, "*.json")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
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

    /// <summary>FS events can fire mid-write; ingest after a short delay on the UI thread.</summary>
    private void ScheduleIngest(string path) =>
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(150);
            IngestSpoolFile(path);
        });

    private void IngestSpoolFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            // project id = the immediate parent folder name under the spool root
            var projectId = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            var dtos = TaskSpool.ReadFile(path, projectId);
            if (dtos.Count == 0) return; // empty/partial write — leave the file; a later event retries
            foreach (var dto in dtos) AllWorkerTasks.Add(new WorkerTaskViewModel(dto));
            try { File.Delete(path); } catch { }
            RefreshTaskViews();
            SaveState();
        }
        catch { }
    }
}
