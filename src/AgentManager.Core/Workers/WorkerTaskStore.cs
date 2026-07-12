using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AgentManager.Core.Workers;

/// <summary>
/// Owns the worker-task domain: the project backlog and per-worker queues, spool ingest, and all
/// lifecycle transitions (backlog → assigned → running → done/failed). Pure logic with no UI or
/// engine dependency, so it is unit-testable. The host (WPF) observes <see cref="Changed"/> to
/// re-render and persist, drives execution via <see cref="NextRunnable"/>, and feeds spool files in
/// through <see cref="IngestFile"/>. Not thread-safe — call on a single thread (the UI thread).
/// </summary>
public sealed class WorkerTaskStore
{
    private readonly List<WorkerTaskDto> _tasks = [];

    /// <summary>SEC (spool DoS guard): max tasks held in the store TOTAL (backlog + all worker queues +
    /// finished history, across every project). Ingest past this is rejected — reject-newest, never evict a
    /// live/assigned task — so a flood of spool files can't grow in-memory + persisted (project.json) state
    /// without bound.</summary>
    public const int MaxTasks = 2000;

    /// <summary>Raised after any mutation (load, ingest, assign, status change, reorder, delete).</summary>
    public event Action? Changed;

    /// <summary>Raised (once per ingest, after the tasks are added) with the number of tasks DROPPED by the
    /// per-file or store cap, so the host can surface it (never a silent loss). Value is always &gt; 0.</summary>
    public event Action<int>? TasksDropped;

    /// <summary>How many tasks the most recent <see cref="IngestFile(string,string,string)"/> call dropped
    /// (per-file + store cap). Lets the caller delete a cap-rejected spool file instead of leaving it to
    /// re-fire the watcher forever.</summary>
    public int LastIngestDropped { get; private set; }

    public IReadOnlyList<WorkerTaskDto> All => _tasks;

    /// <summary>Replace all tasks (e.g. from persisted state). Raises <see cref="Changed"/>.</summary>
    public void Load(IEnumerable<WorkerTaskDto> tasks)
    {
        _tasks.Clear();
        _tasks.AddRange(tasks ?? []);
        Changed?.Invoke();
    }

    /// <summary>A copy for persistence.</summary>
    public IReadOnlyList<WorkerTaskDto> Snapshot() => _tasks.ToList();

    /// <summary>Startup reconciliation: a task left <c>running</c> by a crash can never finish (its
    /// worker is reset to idle on restore), so re-queue it as <c>assigned</c>. Returns the count.</summary>
    public int ReconcileInterrupted()
    {
        var n = 0;
        for (var i = 0; i < _tasks.Count; i++)
            if (_tasks[i].Status == WorkerTaskStatus.Running)
            {
                _tasks[i] = _tasks[i] with { Status = WorkerTaskStatus.Assigned };
                n++;
            }
        if (n > 0) Changed?.Invoke();
        return n;
    }

    // ---- ingest ---------------------------------------------------------

    /// <summary>Parse a spool file (single object or array) and append any valid tasks to the
    /// backlog. The project id is the file's immediate parent folder name. Returns the tasks added
    /// (empty on a partial/garbage read — the caller should leave the file for a later retry).</summary>
    public IReadOnlyList<WorkerTaskDto> IngestFile(string path)
        => IngestFile(path, Path.GetFileName(Path.GetDirectoryName(path) ?? ""), "");

    /// <summary>Ingest with an explicit project id (and optional origin session) — used when the file
    /// lives in a session's <c>.am/worker-tasks/</c> (the skill's fallback) where the parent folder
    /// isn't a project id. <paramref name="originSessionId"/> = the session that wrote it (report target).</summary>
    public IReadOnlyList<WorkerTaskDto> IngestFile(string path, string projectId, string originSessionId = "")
    {
        var result = TaskSpool.ReadFile(path, projectId);
        var parsed = result.Tasks;
        var dropped = result.Dropped; // per-file cap overflow
        if (!string.IsNullOrEmpty(originSessionId))
            parsed = parsed.Select(t => t with { OriginSessionId = originSessionId }).ToList();

        // Store-level cap: reject-newest (drop the incoming surplus). Never evict existing tasks — the
        // oldest may be assigned/running and evicting them would corrupt live worker queues.
        var remaining = Math.Max(0, MaxTasks - _tasks.Count);
        IReadOnlyList<WorkerTaskDto> added;
        if (parsed.Count > remaining)
        {
            added = parsed.Take(remaining).ToList();
            dropped += parsed.Count - remaining;
        }
        else added = parsed;

        LastIngestDropped = dropped;
        if (added.Count > 0) { _tasks.AddRange(added); Changed?.Invoke(); }
        if (dropped > 0) TasksDropped?.Invoke(dropped);
        return added;
    }

    // ---- queries --------------------------------------------------------

    /// <summary>Backlog (unassigned) tasks for a project, oldest first.</summary>
    public IEnumerable<WorkerTaskDto> Backlog(string? projectId) =>
        _tasks.Where(t => t.Status == WorkerTaskStatus.Backlog && (projectId is null || t.ProjectId == projectId))
              .OrderBy(t => t.CreatedUtc, StringComparer.Ordinal);

    /// <summary>One worker's queue (assigned + running) in run order.</summary>
    public IEnumerable<WorkerTaskDto> QueueFor(string workerId) =>
        _tasks.Where(t => t.AssignedWorkerId == workerId && WorkerTaskStatus.IsQueued(t.Status))
              .OrderBy(t => t.Order);

    /// <summary>Every task assigned to a worker (queued, running, done or failed), in order — the
    /// full per-worker list the UI shows. Excludes backlog.</summary>
    public IEnumerable<WorkerTaskDto> AssignedTo(string workerId) =>
        _tasks.Where(t => t.AssignedWorkerId == workerId && t.Status != WorkerTaskStatus.Backlog)
              .OrderBy(t => t.Order);

    /// <summary>Distinct worker ids that own at least one assigned/finished task in a project,
    /// in first-seen order.</summary>
    public IReadOnlyList<string> WorkerIdsWithTasks(string? projectId)
    {
        var seen = new List<string>();
        foreach (var t in _tasks)
            if (t.Status != WorkerTaskStatus.Backlog
                && !string.IsNullOrEmpty(t.AssignedWorkerId)
                && (projectId is null || t.ProjectId == projectId)
                && !seen.Contains(t.AssignedWorkerId))
                seen.Add(t.AssignedWorkerId);
        return seen;
    }

    /// <summary>The next task to run on a worker (first <c>assigned</c> in queue order), or null
    /// if the queue is empty or something is already running on it.</summary>
    public WorkerTaskDto? NextRunnable(string workerId)
    {
        if (_tasks.Any(t => t.AssignedWorkerId == workerId && t.Status == WorkerTaskStatus.Running))
            return null; // one at a time per worker
        return QueueFor(workerId).FirstOrDefault(t => t.Status == WorkerTaskStatus.Assigned);
    }

    public WorkerTaskDto? Find(string taskId) => _tasks.FirstOrDefault(t => t.Id == taskId);

    // ---- mutations ------------------------------------------------------

    /// <summary>Assign a backlog (or failed) task to a worker, appending it to the worker's queue.</summary>
    public void Assign(string taskId, string workerId)
    {
        var i = IndexOf(taskId);
        if (i < 0 || string.IsNullOrEmpty(workerId)) return;
        var nextOrder = _tasks.Where(t => t.AssignedWorkerId == workerId && WorkerTaskStatus.IsQueued(t.Status))
                              .Select(t => t.Order).DefaultIfEmpty(0).Max() + 1;
        _tasks[i] = _tasks[i] with { AssignedWorkerId = workerId, Status = WorkerTaskStatus.Assigned, Order = nextOrder };
        Changed?.Invoke();
    }

    /// <summary>Return a task to the project backlog.</summary>
    public void Unassign(string taskId)
    {
        var i = IndexOf(taskId);
        if (i < 0) return;
        _tasks[i] = _tasks[i] with { AssignedWorkerId = "", Status = WorkerTaskStatus.Backlog, Order = 0 };
        Changed?.Invoke();
    }

    public void SetStatus(string taskId, string status)
    {
        var i = IndexOf(taskId);
        if (i < 0 || _tasks[i].Status == status) return;
        _tasks[i] = _tasks[i] with { Status = status };
        Changed?.Invoke();
    }

    /// <summary>Store the worker's result text on a task (its report back to the origin session).</summary>
    public void SetReport(string taskId, string report)
    {
        var i = IndexOf(taskId);
        if (i < 0) return;
        _tasks[i] = _tasks[i] with { Report = report ?? "" };
        Changed?.Invoke();
    }

    /// <summary>Finished tasks that carry a report for a given origin session — the report inbox feed,
    /// oldest first (so combined reports keep the dispatch order).</summary>
    public IEnumerable<WorkerTaskDto> ReportsForOrigin(string originSessionId) =>
        _tasks.Where(t => t.OriginSessionId == originSessionId
                          && WorkerTaskStatus.IsFinished(t.Status)
                          && !string.IsNullOrEmpty(t.Report)
                          && !t.ReportDismissed)
              .OrderBy(t => t.Order).ThenBy(t => t.CreatedUtc, StringComparer.Ordinal);

    /// <summary>Hide a report from its origin session's inbox (dismiss = handled). The task stays in the
    /// worker's finished history — this only clears the inbox notification.</summary>
    public void DismissReport(string taskId)
    {
        var i = IndexOf(taskId);
        if (i < 0 || _tasks[i].ReportDismissed) return;
        _tasks[i] = _tasks[i] with { ReportDismissed = true, ReportSeen = true };
        Changed?.Invoke();
    }

    /// <summary>Mark one report seen (e.g. after the user copies/opens it) — clears its NEW badge.</summary>
    public void MarkReportSeen(string taskId)
    {
        var i = IndexOf(taskId);
        if (i < 0 || _tasks[i].ReportSeen) return;
        _tasks[i] = _tasks[i] with { ReportSeen = true };
        Changed?.Invoke();
    }

    /// <summary>Mark all of a session's inbox reports seen (clears every NEW badge). Returns true if any changed.</summary>
    public bool MarkReportsSeen(string originSessionId)
    {
        var changed = false;
        for (int i = 0; i < _tasks.Count; i++)
        {
            var t = _tasks[i];
            if (t.OriginSessionId == originSessionId && WorkerTaskStatus.IsFinished(t.Status)
                && !string.IsNullOrEmpty(t.Report) && !t.ReportDismissed && !t.ReportSeen)
            { _tasks[i] = t with { ReportSeen = true }; changed = true; }
        }
        if (changed) Changed?.Invoke();
        return changed;
    }

    public void Delete(string taskId)
    {
        var i = IndexOf(taskId);
        if (i < 0) return;
        _tasks.RemoveAt(i);
        Changed?.Invoke();
    }

    /// <summary>Reorder a queued task within its worker's queue (dir = -1 up / +1 down).</summary>
    public void Move(string taskId, int dir)
    {
        var task = Find(taskId);
        if (task is null || !WorkerTaskStatus.IsQueued(task.Status)) return;
        var queue = QueueFor(task.AssignedWorkerId).ToList();
        var idx = queue.FindIndex(t => t.Id == taskId);
        var swapIdx = idx + Math.Sign(dir);
        if (idx < 0 || swapIdx < 0 || swapIdx >= queue.Count) return;
        var a = queue[idx];
        var b = queue[swapIdx];
        Replace(a.Id, a with { Order = b.Order });
        Replace(b.Id, b with { Order = a.Order });
        Changed?.Invoke();
    }

    /// <summary>Drop finished (done/failed) tasks from a worker's list.</summary>
    public void ClearFinished(string workerId)
    {
        var removed = _tasks.RemoveAll(t => t.AssignedWorkerId == workerId && WorkerTaskStatus.IsFinished(t.Status));
        if (removed > 0) Changed?.Invoke();
    }

    /// <summary>A worker was deleted: drop its finished (done/failed) history and return its still-pending
    /// tasks to the project backlog (work isn't lost — it can be reassigned). Without this, a deleted
    /// worker's tasks linger and render a ghost queue card.</summary>
    public void RemoveWorker(string workerId)
    {
        if (string.IsNullOrEmpty(workerId)) return;
        var changed = _tasks.RemoveAll(t => t.AssignedWorkerId == workerId && WorkerTaskStatus.IsFinished(t.Status)) > 0;
        for (int i = 0; i < _tasks.Count; i++)
        {
            if (_tasks[i].AssignedWorkerId != workerId) continue;
            _tasks[i] = _tasks[i] with { AssignedWorkerId = "", Status = WorkerTaskStatus.Backlog, Order = 0 };
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    private int IndexOf(string taskId) => _tasks.FindIndex(t => t.Id == taskId);

    private void Replace(string taskId, WorkerTaskDto dto)
    {
        var i = IndexOf(taskId);
        if (i >= 0) _tasks[i] = dto;
    }
}
