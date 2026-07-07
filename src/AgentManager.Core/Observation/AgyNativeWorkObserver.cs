using System.Collections.Concurrent;
using System.Text.Json;

namespace AgentManager.Core.Observation;

/// <summary>
/// Passive observer for Antigravity/agy cache files. This intentionally treats
/// the cache schema as best-effort and reports medium-confidence discoveries.
/// </summary>
public sealed class AgyNativeWorkObserver(string? userProfile = null) : INativeWorkObserver
{
    private readonly string _home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly ConcurrentDictionary<string, ObservedWorkItem> _items = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private NativeWorkObservationTarget? _target;
    private string? _conversationId;
    private string? _systemGeneratedPath;

    public string EngineId => "agy";

    public event EventHandler<ObservedWorkItem>? WorkItemChanged;

    public Task StartAsync(NativeWorkObservationTarget target, CancellationToken ct = default)
    {
        _target = target;
        _conversationId = target.EngineSessionId ?? TryReadLastConversationId(target.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(_conversationId)) return Task.CompletedTask;

        _systemGeneratedPath = Path.Combine(_home, ".gemini", "antigravity", "brain", _conversationId, ".system_generated");
        if (!Directory.Exists(_systemGeneratedPath)) return Task.CompletedTask;

        IngestAllTranscripts();
        WatchIfExists(Path.Combine(_systemGeneratedPath, "logs"), "*.jsonl", IngestAllTranscripts);
        WatchIfExists(Path.Combine(_systemGeneratedPath, "messages"), "*.json", IngestMessages);
        WatchIfExists(Path.Combine(_systemGeneratedPath, "tasks"), "*.log", IngestTaskLog);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObservedWorkItem>> SnapshotAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ObservedWorkItem> snapshot = _items.Values
            .OrderByDescending(i => i.LastActivityAt)
            .ToList();
        return Task.FromResult(snapshot);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        return ValueTask.CompletedTask;
    }

    private void WatchIfExists(string directory, string filter, Action ingest)
    {
        if (!Directory.Exists(directory)) return;
        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        FileSystemEventHandler changed = (_, _) => _ = Task.Run(async () =>
        {
            await Task.Delay(80).ConfigureAwait(false);
            ingest();
        });
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Renamed += (_, _) => changed(watcher, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, filter));
        _watchers.Add(watcher);
    }

    private void IngestAllTranscripts()
    {
        if (_systemGeneratedPath is null || _target is null || _conversationId is null) return;
        var logsDir = Path.Combine(_systemGeneratedPath, "logs");
        if (!Directory.Exists(logsDir)) return;
        foreach (var file in Directory.EnumerateFiles(logsDir, "*.jsonl"))
        {
            var lastWrite = File.GetLastWriteTimeUtc(file);
            foreach (var line in ReadLinesShared(file))
            {
                if (!line.Contains("SUBAGENT", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("conversationId", StringComparison.OrdinalIgnoreCase)) continue;
                TryIngestTranscriptLine(line, lastWrite);
            }
        }
    }

    private void IngestMessages()
    {
        if (_systemGeneratedPath is null) return;
        var messages = Path.Combine(_systemGeneratedPath, "messages");
        if (!Directory.Exists(messages)) return;

        foreach (var file in Directory.EnumerateFiles(messages, "*.json"))
        {
            string json;
            try { json = File.ReadAllText(file); } catch { continue; }
            if (string.IsNullOrWhiteSpace(json)) continue;
            TryMarkFromMessage(json, File.GetLastWriteTimeUtc(file));
        }
    }

    private void IngestTaskLog()
    {
        if (_systemGeneratedPath is null || _target is null || _conversationId is null) return;
        var tasks = Path.Combine(_systemGeneratedPath, "tasks");
        if (!Directory.Exists(tasks)) return;

        foreach (var file in Directory.EnumerateFiles(tasks, "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var id = $"agy:{_target.ParentSessionId}:{name}";
            var item = new ObservedWorkItem
            {
                Id = id,
                EngineId = EngineId,
                ParentSessionId = _target.ParentSessionId,
                VendorParentSessionId = _conversationId,
                VendorWorkId = name,
                Kind = WorkItemKind.NativeTask,
                State = ObservedState.Running,
                Source = ObservationSource.FileSystem,
                Confidence = ObservationConfidence.Low,
                DisplayName = name,
                TranscriptPath = file,
                ManagedByAgentManager = _target.ManagedByAgentManager,
                StartedAt = File.GetCreationTimeUtc(file),
                LastActivityAt = File.GetLastWriteTimeUtc(file)
            };
            Upsert(item);
        }
    }

    private void TryIngestTranscriptLine(string line, DateTime lastWriteUtc)
    {
        if (_target is null || _conversationId is null) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!ContainsText(root, "INVOKE_SUBAGENT") && !ContainsText(root, "invoke_subagent")) return;

            var childId = FindString(root, "conversationId") ?? FindString(root, "conversation_id") ?? FindString(root, "id");
            if (string.IsNullOrWhiteSpace(childId)) return;

            var workspace = FindFirstStringInArray(root, "workspaceUris") ?? FindString(root, "workspaceUri");
            var logUri = FindString(root, "logAbsoluteUri") ?? FindString(root, "logUri");
            var role = FindString(root, "role") ?? FindString(root, "agentRole") ?? FindString(root, "type");
            var when = new DateTimeOffset(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc));
            var item = new ObservedWorkItem
            {
                Id = $"agy:{_target.ParentSessionId}:{childId}",
                EngineId = EngineId,
                ParentSessionId = _target.ParentSessionId,
                VendorParentSessionId = _conversationId,
                VendorWorkId = childId,
                AgentId = childId,
                Kind = WorkItemKind.NativeSubagent,
                State = ObservedState.Running,
                Source = ObservationSource.Transcript,
                Confidence = ObservationConfidence.Medium,
                AgentType = role,
                DisplayName = role ?? "Antigravity subagent",
                Cwd = UriToPath(workspace),
                TranscriptPath = UriToPath(logUri),
                ManagedByAgentManager = _target.ManagedByAgentManager,
                StartedAt = when,
                LastActivityAt = when,
                RawJson = line
            };
            Upsert(item);
        }
        catch
        {
        }
    }

    private void TryMarkFromMessage(string json, DateTime lastWriteUtc)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var text = root.ToString();
            var childId = FindString(root, "conversationId") ?? FindString(root, "sender") ?? FindString(root, "recipient");
            if (string.IsNullOrWhiteSpace(childId)) return;

            var match = _items.Values.FirstOrDefault(i =>
                string.Equals(i.VendorWorkId, childId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(i.VendorWorkId) && childId.Contains(i.VendorWorkId, StringComparison.OrdinalIgnoreCase)));
            if (match is null) return;

            var state = text.Contains("fail", StringComparison.OrdinalIgnoreCase)
                || text.Contains("error", StringComparison.OrdinalIgnoreCase)
                    ? ObservedState.Failed
                    : text.Contains("complete", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("done", StringComparison.OrdinalIgnoreCase)
                        ? ObservedState.Completed
                        : match.State;
            if (state == match.State) return;

            var when = new DateTimeOffset(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc));
            Upsert(match with
            {
                State = state,
                Source = ObservationSource.FileSystem,
                Confidence = ObservationConfidence.Medium,
                LastActivityAt = when,
                CompletedAt = state is ObservedState.Completed or ObservedState.Failed ? when : match.CompletedAt,
                LastMessage = FindString(root, "content") ?? match.LastMessage,
                RawJson = json
            });
        }
        catch
        {
        }
    }

    private void Upsert(ObservedWorkItem item)
    {
        var merged = _items.AddOrUpdate(
            item.Id,
            item,
            (_, existing) => existing with
            {
                State = item.State == ObservedState.Unknown ? existing.State : item.State,
                Source = item.Source,
                Confidence = item.Confidence,
                VendorParentSessionId = item.VendorParentSessionId ?? existing.VendorParentSessionId,
                VendorWorkId = item.VendorWorkId ?? existing.VendorWorkId,
                AgentId = item.AgentId ?? existing.AgentId,
                AgentType = item.AgentType ?? existing.AgentType,
                DisplayName = item.DisplayName ?? existing.DisplayName,
                Cwd = item.Cwd ?? existing.Cwd,
                TranscriptPath = item.TranscriptPath ?? existing.TranscriptPath,
                LastMessage = item.LastMessage ?? existing.LastMessage,
                Error = item.Error ?? existing.Error,
                RawJson = item.RawJson ?? existing.RawJson,
                ManagedByAgentManager = item.ManagedByAgentManager || existing.ManagedByAgentManager,
                StartedAt = existing.StartedAt == default ? item.StartedAt : existing.StartedAt,
                LastActivityAt = item.LastActivityAt,
                CompletedAt = item.CompletedAt ?? existing.CompletedAt
            });
        WorkItemChanged?.Invoke(this, merged);
    }

    private string? TryReadLastConversationId(string workingDirectory)
    {
        var path = Path.Combine(_home, ".gemini", "antigravity-cli", "cache", "last_conversations.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var wanted = Path.GetFullPath(workingDirectory).TrimEnd('\\', '/');
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!PathLooksEqual(prop.Name, wanted)) continue;
                if (prop.Value.ValueKind == JsonValueKind.String) return prop.Value.GetString();
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    return FindString(prop.Value, "conversationId") ?? FindString(prop.Value, "id");
            }
        }
        catch
        {
        }
        return null;
    }

    private static bool PathLooksEqual(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), right, StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left.TrimEnd('\\', '/'), right, StringComparison.OrdinalIgnoreCase); }
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    private static string? UriToPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : value;
    }

    private static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                    return prop.Value.GetString();
                var nested = FindString(prop.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static string? FindFirstStringInArray(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String) return item.GetString();
                }
                var nested = FindFirstStringInArray(prop.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstStringInArray(item, name);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static bool ContainsText(JsonElement element, string text)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(p => ContainsText(p.Value, text));
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(i => ContainsText(i, text));
        return false;
    }
}
