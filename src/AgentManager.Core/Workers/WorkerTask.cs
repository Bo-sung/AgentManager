using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgentManager.Core.Workers;

/// <summary>A delegation task in the project backlog. The worker-prompt skill writes these to the
/// task spool (as {title, prompt, engine}); AgentManager ingests them, then the user assigns each
/// to a worker. Persisted in app state.</summary>
public sealed record WorkerTaskDto
{
    public required string Id { get; init; }
    public string ProjectId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Prompt { get; init; } = "";
    /// <summary>Engine the skill suggested for this task (cc|gx|agy|pi), or empty.</summary>
    public string Engine { get; init; } = "";
    /// <summary>backlog | assigned | running | done | failed (see <see cref="WorkerTaskStatus"/>).</summary>
    public string Status { get; init; } = WorkerTaskStatus.Backlog;
    /// <summary>Worker session id this task is assigned to (empty = still in backlog).</summary>
    public string AssignedWorkerId { get; init; } = "";
    /// <summary>Position in the assigned worker's queue (ascending; 0 while in backlog).</summary>
    public int Order { get; init; }
    /// <summary>Session that originated this task (whose skill wrote it); the worker reports back here.</summary>
    public string OriginSessionId { get; init; } = "";
    /// <summary>The worker's result text, captured when the task finishes (empty until done).</summary>
    public string Report { get; init; } = "";
    /// <summary>Report inbox: the user has seen this report (false → shows a NEW badge).</summary>
    public bool ReportSeen { get; init; }
    /// <summary>Report inbox: dismissed from the inbox by the user (still kept in the worker's history).</summary>
    public bool ReportDismissed { get; init; }
    public string CreatedUtc { get; init; } = DateTime.UtcNow.ToString("o");
}

/// <summary>Worker task lifecycle states (persisted as strings for JSON stability).</summary>
public static class WorkerTaskStatus
{
    public const string Backlog = "backlog";   // in the project backlog, unassigned
    public const string Assigned = "assigned"; // queued in a worker's list, waiting to run
    public const string Running = "running";   // currently executing on its worker
    public const string Done = "done";
    public const string Failed = "failed";

    /// <summary>States that occupy a worker's queue (assigned or running).</summary>
    public static bool IsQueued(string s) => s is Assigned or Running;
    /// <summary>Terminal states.</summary>
    public static bool IsFinished(string s) => s is Done or Failed;
}

/// <summary>The minimal shape the skill writes to a spool file (AgentManager fills in the rest).</summary>
file sealed record SpoolTask
{
    public string? title { get; init; }
    public string? prompt { get; init; }
    public string? engine { get; init; }
}

/// <summary>Result of reading one spool file: the accepted tasks plus how many valid tasks were
/// DROPPED by the per-file cap (<see cref="TaskSpool.MaxTasksPerFile"/>) so the caller can surface the
/// drop instead of silently losing work.</summary>
public sealed record SpoolReadResult(IReadOnlyList<WorkerTaskDto> Tasks, int Dropped);

/// <summary>
/// Project task spool: a per-project directory the agent (via the worker-prompt skill) drops
/// task JSON files into. One watcher on the root ingests them into the backlog and deletes them.
/// Layout: <c>&lt;root&gt;/&lt;projectId&gt;/&lt;anything&gt;.json</c>.
/// </summary>
public static class TaskSpool
{
    /// <summary>SEC (spool DoS guard): max tasks accepted from a SINGLE spool file. A giant JSON array
    /// (one file can hold hundreds of thousands of minimal objects within the byte cap) is rejected past
    /// this count — reject-newest, never evict — and the overflow is reported as dropped, never silently.</summary>
    public const int MaxTasksPerFile = 200;

    /// <summary>Spool root under app data. Per-project subdir = the agent's AGENTMANAGER_TASK_SPOOL.</summary>
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "task-spool");

    public static string DirFor(string projectId) => Path.Combine(Root, Sanitize(projectId));

    /// <summary>Read one spool file into backlog WorkerTaskDtos. The file may hold a single task
    /// object or a JSON array of tasks; returns all valid tasks (empty if unreadable/partial), capped at
    /// <see cref="MaxTasksPerFile"/> with the surplus counted in <see cref="SpoolReadResult.Dropped"/>.</summary>
    public static SpoolReadResult ReadFile(string path, string projectId)
    {
        try
        {
            var json = (JsonFile.ReadCapped(path) ?? "").Trim(); // size-capped read (SEC: spool DoS guard)
            if (json.Length == 0) return new SpoolReadResult([], 0);
            var raw = json[0] == '['
                ? JsonSerializer.Deserialize<List<SpoolTask>>(json) ?? []
                : [JsonSerializer.Deserialize<SpoolTask>(json)!];
            var tasks = new List<WorkerTaskDto>();
            var dropped = 0;
            foreach (var st in raw)
            {
                if (st is null || string.IsNullOrWhiteSpace(st.prompt)) continue;
                if (tasks.Count >= MaxTasksPerFile) { dropped++; continue; } // per-file cap: reject the surplus
                tasks.Add(new WorkerTaskDto
                {
                    Id = "t" + Guid.NewGuid().ToString("N")[..12],
                    ProjectId = projectId,
                    Title = string.IsNullOrWhiteSpace(st.title) ? FirstLine(st.prompt!) : st.title!.Trim(),
                    Prompt = st.prompt!.Trim(),
                    Engine = (st.engine ?? "").Trim().ToLowerInvariant(),
                    Status = WorkerTaskStatus.Backlog,
                });
            }
            return new SpoolReadResult(tasks, dropped);
        }
        catch { return new SpoolReadResult([], 0); }
    }

    static string FirstLine(string s)
    {
        var line = s.Replace("\r", "").Split('\n', 2)[0].Trim();
        return line.Length <= 60 ? line : line[..59] + "…";
    }

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
