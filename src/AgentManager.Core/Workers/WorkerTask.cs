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
    /// <summary>backlog | assigned | running | done | failed</summary>
    public string Status { get; init; } = "backlog";
    /// <summary>Worker session id this task is assigned to (empty = still in backlog).</summary>
    public string AssignedWorkerId { get; init; } = "";
    public string CreatedUtc { get; init; } = DateTime.UtcNow.ToString("o");
}

/// <summary>The minimal shape the skill writes to a spool file (AgentManager fills in the rest).</summary>
file sealed record SpoolTask
{
    public string? title { get; init; }
    public string? prompt { get; init; }
    public string? engine { get; init; }
}

/// <summary>
/// Project task spool: a per-project directory the agent (via the worker-prompt skill) drops
/// task JSON files into. One watcher on the root ingests them into the backlog and deletes them.
/// Layout: <c>&lt;root&gt;/&lt;projectId&gt;/&lt;anything&gt;.json</c>.
/// </summary>
public static class TaskSpool
{
    /// <summary>Spool root under app data. Per-project subdir = the agent's AGENTMANAGER_TASK_SPOOL.</summary>
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "task-spool");

    public static string DirFor(string projectId) => Path.Combine(Root, Sanitize(projectId));

    /// <summary>Read one spool file into a WorkerTaskDto (status=backlog). Null if unreadable/empty.
    /// The file may contain a single task object or a JSON array of tasks (returns the first here;
    /// use <see cref="Drain"/> for arrays).</summary>
    public static IReadOnlyList<WorkerTaskDto> ReadFile(string path, string projectId)
    {
        try
        {
            var json = File.ReadAllText(path).Trim();
            if (json.Length == 0) return [];
            var raw = json[0] == '['
                ? JsonSerializer.Deserialize<List<SpoolTask>>(json) ?? []
                : [JsonSerializer.Deserialize<SpoolTask>(json)!];
            var tasks = new List<WorkerTaskDto>();
            foreach (var st in raw)
            {
                if (st is null || string.IsNullOrWhiteSpace(st.prompt)) continue;
                tasks.Add(new WorkerTaskDto
                {
                    Id = "t" + Guid.NewGuid().ToString("N")[..12],
                    ProjectId = projectId,
                    Title = string.IsNullOrWhiteSpace(st.title) ? FirstLine(st.prompt!) : st.title!.Trim(),
                    Prompt = st.prompt!.Trim(),
                    Engine = (st.engine ?? "").Trim().ToLowerInvariant(),
                    Status = "backlog",
                });
            }
            return tasks;
        }
        catch { return []; }
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
