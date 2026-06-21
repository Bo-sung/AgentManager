namespace AgentManager.Core.Observation;

/// <summary>여러 INativeWorkObserver 를 하나처럼 구동(이벤트 합류, 스냅샷 합집합).</summary>
public sealed class CompositeNativeWorkObserver(string engineId, params INativeWorkObserver[] children) : INativeWorkObserver
{
    private readonly INativeWorkObserver[] _children = children;

    public string EngineId { get; } = engineId;

    public event EventHandler<ObservedWorkItem>? WorkItemChanged;

    public async Task StartAsync(NativeWorkObservationTarget target, CancellationToken ct = default)
    {
        foreach (var child in _children)
        {
            child.WorkItemChanged += OnChildChanged;
            await child.StartAsync(target, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<ObservedWorkItem>> SnapshotAsync(CancellationToken ct = default)
    {
        var all = new List<ObservedWorkItem>();
        foreach (var child in _children)
            all.AddRange(await child.SnapshotAsync(ct).ConfigureAwait(false));
        return all;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var child in _children)
        {
            child.WorkItemChanged -= OnChildChanged;
            await child.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnChildChanged(object? sender, ObservedWorkItem item) => WorkItemChanged?.Invoke(this, item);
}
