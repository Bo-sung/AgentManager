using System.Diagnostics;
using AgentManager.Core.Events;

namespace AgentManager.Core.Agents;

/// <summary>What an engine supports — UI toggles/panels can adapt to this.</summary>
public sealed record AgentCapabilities(
    bool Permissions,
    bool Thinking,
    bool Sessions,
    bool Images,
    bool TokenUsage,
    bool Quota);

/// <summary>Options for starting one agent turn/session.</summary>
/// <summary>User's verdict on a tool-permission request.</summary>
public sealed record PermissionDecision(bool Allow, string? Reason = null);

/// <summary>How much the engine may touch without asking. Mapping is engine-specific:
/// Codex maps to --sandbox read-only/workspace-write/danger; Claude (no approval broker yet)
/// treats ReadOnly as plan mode and everything else as bypass.</summary>
public enum SandboxMode { ReadOnly, WorkspaceWrite, DangerFullAccess }

public sealed record SessionOptions
{
    public required string WorkingDirectory { get; init; }
    public string? Model { get; init; }
    public string? ResumeSessionId { get; init; }
    /// <summary>Skip approval prompts (yolo / bypass sandbox).</summary>
    public bool BypassPermissions { get; init; }
    public SandboxMode Sandbox { get; init; } = SandboxMode.DangerFullAccess;
    /// <summary>MCP passthrough: path to a user-managed MCP config file (Claude --mcp-config).</summary>
    public string? McpConfigPath { get; init; }
    /// <summary>Image file paths to attach to this turn (Claude: base64 blocks; Codex: -i args).</summary>
    public IReadOnlyList<string> Images { get; init; } = [];
    /// <summary>멀티폴더 project: 주 폴더(WorkingDirectory) 외에 에이전트가 접근할 루트들.
    /// Claude: --add-dir 반복; Codex: workspace-write 샌드박스의 writable_roots 오버라이드.</summary>
    public IReadOnlyList<string> AdditionalDirectories { get; init; } = [];
    /// <summary>코덱스 추론 강도(low/medium/high/xhigh). null/빈값 = 엔진 기본. Claude는 무시.</summary>
    public string? ReasoningEffort { get; init; }
}

/// <summary>
/// Adapts one CLI engine: how to spawn it, how to feed the prompt, and how to turn
/// its stdout JSONL into normalized events. Implementations must be stateless w.r.t.
/// process I/O — the <see cref="Session.AgentSession"/> owns the process.
/// </summary>
public interface IAgentAdapter
{
    string Id { get; }
    AgentCapabilities Capabilities { get; }

    /// <summary>Codex exec hangs unless stdin is closed after the prompt; Claude keeps stdin open.</summary>
    bool CloseStdinAfterStart { get; }

    /// <summary>턴 완료 후에도 살아있는 서버형 엔진(codex app-server)은 stdin 종료만으로 안 끝날 수 있다 —
    /// true면 AgentSession이 TurnCompleted 직후 프로세스를 종료한다.</summary>
    bool KillAfterTurnCompleted => false;

    /// <summary>Build the process start info. <paramref name="prompt"/> is already translated to English.</summary>
    ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt);

    /// <summary>Lines to write to stdin right after start (Claude: init + user message; Codex: none).</summary>
    IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options);

    /// <summary>Parse one line of stdout JSONL into zero or more normalized events.</summary>
    IEnumerable<NormalizedEvent> ParseLine(string line);

    /// <summary>Format the stdin line answering a permission request, or null if the engine
    /// has no interactive approval protocol (Codex exec). Claude: control_response JSON.</summary>
    string? BuildPermissionResponse(Events.PermissionRequest request, PermissionDecision decision) => null;
}
