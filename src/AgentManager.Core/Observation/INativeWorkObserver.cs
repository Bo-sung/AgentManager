namespace AgentManager.Core.Observation;

public sealed record NativeWorkObservationTarget(
    string EngineId,
    string ParentSessionId,
    string WorkingDirectory,
    string? EngineSessionId = null,
    bool ManagedByAgentManager = true);

/// <summary>
/// Observes engine-native workers without owning their execution. Implementations
/// may use hooks, JSON-RPC events, transcript tailing, or vendor cache files.
/// </summary>
public interface INativeWorkObserver : IAsyncDisposable
{
    string EngineId { get; }

    Task StartAsync(NativeWorkObservationTarget target, CancellationToken ct = default);

    Task<IReadOnlyList<ObservedWorkItem>> SnapshotAsync(CancellationToken ct = default);

    event EventHandler<ObservedWorkItem>? WorkItemChanged;
}
