namespace AgentManager.Core.Observation;

public enum WorkItemKind
{
    Session,
    NativeSubagent,
    NativeBackgroundSession,
    NativeTask,
    AgentManagerWorker
}

public enum ObservedState
{
    Unknown,
    Starting,
    Running,
    Waiting,
    WaitingPermission,
    Completed,
    Failed,
    Stopped
}

public enum ObservationSource
{
    Unknown,
    Hook,
    AppServerEvent,
    ExecJson,
    Transcript,
    FileSystem,
    ProcessPoll
}

public enum ObservationConfidence
{
    Low,
    Medium,
    High
}

/// <summary>
/// Engine-agnostic view of native subagents, background sessions, and future
/// AgentManager-owned workers.
/// </summary>
public sealed record ObservedWorkItem
{
    public required string Id { get; init; }
    public required string EngineId { get; init; }
    public required string ParentSessionId { get; init; }
    public string? VendorParentSessionId { get; init; }
    public string? VendorWorkId { get; init; }
    public string? AgentId { get; init; }

    public WorkItemKind Kind { get; init; } = WorkItemKind.NativeSubagent;
    public ObservedState State { get; init; } = ObservedState.Unknown;
    public ObservationSource Source { get; init; } = ObservationSource.Unknown;
    public ObservationConfidence Confidence { get; init; } = ObservationConfidence.Low;

    public string? AgentType { get; init; }
    public string? DisplayName { get; init; }
    public string? Cwd { get; init; }
    public string? TranscriptPath { get; init; }
    public string? AgentTranscriptPath { get; init; }
    public string? LastMessage { get; init; }
    public string? Error { get; init; }
    public string? RawJson { get; init; }

    public bool ManagedByAgentManager { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
