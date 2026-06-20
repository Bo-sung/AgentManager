using System;

namespace AgentManager.Core.Scheduling;

public sealed record ScheduledJob
{
    public required string Id { get; init; }
    public required string AgentId { get; init; } // "cc", "gx", "agy"
    public string ProjectId { get; init; } = "";
    public string ProjectPath { get; init; } = "";
    public required string Title { get; init; }
    public required string Prompt { get; init; }
    public required string TargetBranch { get; init; }
    public required ScheduleTrigger Trigger { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTime? LastRunUtc { get; init; }

    // NextRunUtc 계산 속성
    public DateTime? NextRunUtc => Enabled ? Trigger.GetNextRunUtc(LastRunUtc) : null;
}
