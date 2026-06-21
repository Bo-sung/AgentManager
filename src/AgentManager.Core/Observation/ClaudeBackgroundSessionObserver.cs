using System.Collections.Concurrent;

namespace AgentManager.Core.Observation;

/// <summary>
/// `claude agents --json` 를 주기적으로 폴링해 머신에서 도는 *다른* claude 세션을
/// 백그라운드 작업자로 노출한다. 훅(in-session subagent)이 못 잡는 별도 세션 보완용.
///
/// 필터: 관리 중인 세션(EngineSessionId) 자신은 제외하고, 같은 작업 디렉터리 트리의
/// 세션만 노출(다른 프로젝트의 무관한 세션 노이즈 방지). 폴에서 사라지면 Stopped 로 마감.
/// </summary>
public sealed class ClaudeBackgroundSessionObserver(string exePath, TimeSpan? interval = null) : INativeWorkObserver
{
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(8);
    private readonly ConcurrentDictionary<string, ObservedWorkItem> _items = new();
    private NativeWorkObservationTarget? _target;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public string EngineId => "cc";

    public event EventHandler<ObservedWorkItem>? WorkItemChanged;

    public Task StartAsync(NativeWorkObservationTarget target, CancellationToken ct = default)
    {
        _target = target;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => PollLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObservedWorkItem>> SnapshotAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ObservedWorkItem> snapshot = _items.Values
            .OrderByDescending(i => i.LastActivityAt)
            .ToList();
        return Task.FromResult(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); } catch { }
        }
        _cts?.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { PollOnce(await ClaudeAgentsProbe.RunAsync(exePath, _target?.WorkingDirectory, ct).ConfigureAwait(false)); }
            catch { /* 한 번 실패해도 계속 */ }
            try { await Task.Delay(_interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void PollOnce(IReadOnlyList<ClaudeAgentRow> rows)
    {
        if (_target is null) return;
        var now = DateTimeOffset.UtcNow;
        var live = new HashSet<string>();

        foreach (var row in rows)
        {
            if (IsManagedSelf(row) || !SameWorkingTree(row.Cwd)) continue;
            var id = $"cc-bg:{row.SessionId}";
            live.Add(id);
            var started = row.StartedAtUnixMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtUnixMs)
                : now;
            Upsert(new ObservedWorkItem
            {
                Id = id,
                EngineId = EngineId,
                ParentSessionId = _target.ParentSessionId,
                VendorParentSessionId = row.SessionId,
                VendorWorkId = row.SessionId,
                Kind = WorkItemKind.NativeBackgroundSession,
                State = ObservedState.Running,
                Source = ObservationSource.ProcessPoll,
                Confidence = ObservationConfidence.High,
                DisplayName = $"claude {row.Kind}",
                Cwd = row.Cwd,
                ManagedByAgentManager = false,
                StartedAt = started,
                LastActivityAt = now,
            });
        }

        // 이번 폴에서 사라진 세션 = 종료됨.
        foreach (var (id, item) in _items)
        {
            if (live.Contains(id) || item.State is ObservedState.Stopped or ObservedState.Completed) continue;
            Upsert(item with { State = ObservedState.Stopped, LastActivityAt = now, CompletedAt = now });
        }
    }

    private bool IsManagedSelf(ClaudeAgentRow row)
        => _target?.EngineSessionId is { Length: > 0 } sid
           && string.Equals(row.SessionId, sid, StringComparison.OrdinalIgnoreCase);

    /// <summary>row.cwd 가 관측 대상 작업 디렉터리와 같은 트리(상호 접두) 인가.</summary>
    private bool SameWorkingTree(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(_target?.WorkingDirectory) || string.IsNullOrWhiteSpace(cwd)) return false;
        var a = Normalize(_target!.WorkingDirectory);
        var b = Normalize(cwd!);
        return a == b || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                      || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch { return path.TrimEnd('\\', '/'); }
    }

    private void Upsert(ObservedWorkItem item)
    {
        var merged = _items.AddOrUpdate(item.Id, item, (_, existing) => existing with
        {
            State = item.State,
            Source = item.Source,
            Confidence = item.Confidence,
            DisplayName = item.DisplayName ?? existing.DisplayName,
            Cwd = item.Cwd ?? existing.Cwd,
            StartedAt = existing.StartedAt == default ? item.StartedAt : existing.StartedAt,
            LastActivityAt = item.LastActivityAt,
            CompletedAt = item.CompletedAt ?? existing.CompletedAt,
        });
        WorkItemChanged?.Invoke(this, merged);
    }
}
