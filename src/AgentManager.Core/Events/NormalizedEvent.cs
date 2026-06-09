namespace AgentManager.Core.Events;

/// <summary>
/// Engine-agnostic event. Each adapter (Claude, Codex, …) converts its own wire
/// schema into these so the translation/UI layers never depend on a specific engine.
/// </summary>
public abstract record NormalizedEvent;

/// <summary>Session/thread started. (Claude system/init, Codex thread.started)</summary>
public sealed record SessionStarted(string SessionId, string? Model, int ToolCount, string? Cwd) : NormalizedEvent;

/// <summary>Assistant natural-language text. This is the EN→KO translation target.</summary>
public sealed record AssistantText(string Text, bool FromSubagent = false, string? OriginalText = null) : NormalizedEvent;

/// <summary>Model reasoning/thinking (Claude thinking block). Usually not translated.</summary>
public sealed record Thinking(string Text) : NormalizedEvent;

/// <summary>A tool/command invocation began.</summary>
public sealed record ToolUseStarted(string ToolUseId, string Name, string InputJson) : NormalizedEvent;

/// <summary>A tool/command result. Only subagent (Task) results are translated.</summary>
public sealed record ToolResult(string ToolUseId, string Content, bool IsError, bool FromSubagent = false, string? OriginalContent = null) : NormalizedEvent;

/// <summary>Running token usage.</summary>
public sealed record TokenUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens = 0,
    long CacheCreationTokens = 0,
    long ReasoningTokens = 0) : NormalizedEvent;

/// <summary>Quota/rate-limit snapshot (Claude rate_limit_event) — for the manager dashboard.</summary>
public sealed record QuotaUpdate(double Utilization, long ResetsAtUnix, string RateLimitType, string Status) : NormalizedEvent;

/// <summary>Engine is asking permission to run a tool (Claude can_use_tool).</summary>
public sealed record PermissionRequest(string RequestId, string ToolName, string InputJson, string? ToolUseId) : NormalizedEvent;

/// <summary>Turn finished.</summary>
public sealed record TurnCompleted(string? FinalText, bool IsError, double? CostUsd, int? NumTurns) : NormalizedEvent;

/// <summary>Engine error (stderr or error event).</summary>
public sealed record EngineError(string Message) : NormalizedEvent;

/// <summary>Unrecognized event type — kept for forward-compat, safely ignored by consumers.</summary>
public sealed record RawUnknown(string Type, string Raw) : NormalizedEvent;
