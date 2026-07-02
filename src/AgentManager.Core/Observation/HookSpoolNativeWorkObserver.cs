using System.Collections.Concurrent;

namespace AgentManager.Core.Observation;

/// <summary>
/// Reads JSON files written by vendor hook scripts and exposes them as
/// engine-agnostic observed work items.
/// </summary>
public sealed class HookSpoolNativeWorkObserver(string engineId, string spoolDirectory) : INativeWorkObserver
{
    private readonly ConcurrentDictionary<string, ObservedWorkItem> _items = new();
    private FileSystemWatcher? _watcher;
    private NativeWorkObservationTarget? _target;

    public string EngineId { get; } = engineId;

    public event EventHandler<ObservedWorkItem>? WorkItemChanged;

    public Task StartAsync(NativeWorkObservationTarget target, CancellationToken ct = default)
    {
        _target = target;
        Directory.CreateDirectory(spoolDirectory);

        foreach (var file in Directory.EnumerateFiles(spoolDirectory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            TryIngestFile(file);
        }

        _watcher = new FileSystemWatcher(spoolDirectory, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnFileChanged;
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
        if (_watcher is not null)
        {
            _watcher.Created -= OnFileChanged;
            _watcher.Changed -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
        => _ = Task.Run(async () =>
        {
            // Hook writers may still hold the file when the watcher fires.
            await Task.Delay(50).ConfigureAwait(false);
            TryIngestFile(e.FullPath);
        });

    private void TryIngestFile(string path)
    {
        if (!File.Exists(path)) return;

        string json;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > JsonFile.MaxReadBytes) return; // size-capped (SEC: spool DoS guard)
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json)) return;
        var hookEvent = NativeHookEvent.TryParse(EngineId, json);
        if (hookEvent is null) return;
        if (_target is not null && !ParentMatchesTarget(hookEvent.ParentSessionId)) return;

        var observed = ApplyFailureInference(hookEvent.ToObservedWorkItem(
            _target?.ParentSessionId,
            _target?.ManagedByAgentManager ?? false), hookEvent);
        var item = Merge(observed);
        WorkItemChanged?.Invoke(this, item);
    }

    /// <summary>훅이 종료를 Completed로 보고했어도 subagent transcript(또는 last_assistant_message)에
    /// API 오류/한도 종료가 보이면 Failed 로 보정한다. 실패/rate-limit subagent 가시화용.</summary>
    private static ObservedWorkItem ApplyFailureInference(ObservedWorkItem item, NativeHookEvent ev)
    {
        var failure = SubagentTranscriptInspector.Inspect(ev.AgentTranscriptPath ?? "");
        if (!failure.Failed && SubagentTranscriptInspector.LooksLikeLimit(ev.LastAssistantMessage))
            failure = new SubagentFailure(true, true, ev.LastAssistantMessage);
        if (!failure.Failed) return item;

        return item with
        {
            State = ObservedState.Failed,
            Error = failure.Message ?? item.Error,
            LastMessage = failure.Message ?? item.LastMessage,
            CompletedAt = item.CompletedAt ?? item.LastActivityAt,
        };
    }

    private bool ParentMatchesTarget(string parentSessionId)
    {
        if (_target is null) return true;
        if (string.IsNullOrWhiteSpace(_target.EngineSessionId)) return true;
        return string.Equals(parentSessionId, _target.ParentSessionId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parentSessionId, _target.EngineSessionId, StringComparison.OrdinalIgnoreCase);
    }

    private ObservedWorkItem Merge(ObservedWorkItem incoming)
    {
        return _items.AddOrUpdate(
            incoming.Id,
            incoming,
            (_, existing) => existing with
            {
                State = incoming.State == ObservedState.Unknown ? existing.State : incoming.State,
                Source = incoming.Source,
                Confidence = incoming.Confidence,
                VendorParentSessionId = incoming.VendorParentSessionId ?? existing.VendorParentSessionId,
                AgentType = incoming.AgentType ?? existing.AgentType,
                DisplayName = incoming.DisplayName ?? existing.DisplayName,
                Cwd = incoming.Cwd ?? existing.Cwd,
                TranscriptPath = incoming.TranscriptPath ?? existing.TranscriptPath,
                AgentTranscriptPath = incoming.AgentTranscriptPath ?? existing.AgentTranscriptPath,
                LastMessage = incoming.LastMessage ?? existing.LastMessage,
                Error = incoming.Error ?? existing.Error,
                RawJson = incoming.RawJson ?? existing.RawJson,
                ManagedByAgentManager = incoming.ManagedByAgentManager || existing.ManagedByAgentManager,
                StartedAt = existing.StartedAt == default ? incoming.StartedAt : existing.StartedAt,
                LastActivityAt = incoming.LastActivityAt,
                CompletedAt = incoming.CompletedAt ?? existing.CompletedAt
            });
    }
}
