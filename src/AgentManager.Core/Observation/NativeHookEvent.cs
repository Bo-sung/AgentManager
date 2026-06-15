using System.Text.Json;

namespace AgentManager.Core.Observation;

public sealed record NativeHookEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string EngineId { get; init; }
    public required string Event { get; init; }
    public required string ParentSessionId { get; init; }
    public string? TurnId { get; init; }
    public string? AgentId { get; init; }
    public string? AgentType { get; init; }
    public string? Cwd { get; init; }
    public string? TranscriptPath { get; init; }
    public string? AgentTranscriptPath { get; init; }
    public string? LastAssistantMessage { get; init; }
    public string? PermissionMode { get; init; }
    public string? BackgroundTasksJson { get; init; }
    public string? SessionCronsJson { get; init; }
    public string? RawJson { get; init; }

    public ObservedWorkItem ToObservedWorkItem(string? parentSessionIdOverride = null, bool managedByAgentManager = false)
    {
        var isStop = Event.Equals("SubagentStop", StringComparison.OrdinalIgnoreCase);
        var isStart = Event.Equals("SubagentStart", StringComparison.OrdinalIgnoreCase);
        var workId = !string.IsNullOrWhiteSpace(AgentId)
            ? $"{EngineId}:{ParentSessionId}:{AgentId}"
            : $"{EngineId}:{ParentSessionId}:{Event}:{Timestamp.ToUnixTimeMilliseconds()}";

        return new ObservedWorkItem
        {
            Id = workId,
            EngineId = EngineId,
            ParentSessionId = parentSessionIdOverride ?? ParentSessionId,
            VendorParentSessionId = ParentSessionId,
            VendorWorkId = AgentId,
            AgentId = AgentId,
            Kind = WorkItemKind.NativeSubagent,
            State = isStop ? ObservedState.Completed : isStart ? ObservedState.Running : ObservedState.Unknown,
            Source = ObservationSource.Hook,
            Confidence = ObservationConfidence.High,
            AgentType = AgentType,
            DisplayName = AgentType,
            Cwd = Cwd,
            TranscriptPath = TranscriptPath,
            AgentTranscriptPath = AgentTranscriptPath,
            LastMessage = LastAssistantMessage,
            RawJson = RawJson,
            ManagedByAgentManager = managedByAgentManager,
            StartedAt = isStart ? Timestamp : default,
            LastActivityAt = Timestamp,
            CompletedAt = isStop ? Timestamp : null
        };
    }

    public static NativeHookEvent? TryParse(string engineId, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var eventName = Str(root, "hook_event_name") ?? Str(root, "event");
            var sessionId = Str(root, "session_id") ?? Str(root, "sessionId") ?? Str(root, "parentSessionId");
            if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(sessionId)) return null;

            return new NativeHookEvent
            {
                EngineId = engineId,
                Event = eventName!,
                ParentSessionId = sessionId!,
                TurnId = Str(root, "turn_id") ?? Str(root, "turnId"),
                AgentId = Str(root, "agent_id") ?? Str(root, "agentId"),
                AgentType = Str(root, "agent_type") ?? Str(root, "agentType"),
                Cwd = Str(root, "cwd"),
                TranscriptPath = Str(root, "transcript_path") ?? Str(root, "transcriptPath"),
                AgentTranscriptPath = Str(root, "agent_transcript_path") ?? Str(root, "agentTranscriptPath"),
                LastAssistantMessage = Str(root, "last_assistant_message") ?? Str(root, "lastAssistantMessage"),
                PermissionMode = Str(root, "permission_mode") ?? Str(root, "permissionMode"),
                BackgroundTasksJson = Raw(root, "background_tasks") ?? Raw(root, "backgroundTasks"),
                SessionCronsJson = Raw(root, "session_crons") ?? Raw(root, "sessionCrons"),
                RawJson = json
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Raw(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetRawText() : null;
}
