using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AgentManager.Core.Events;
using static AgentManager.Core.Agents.AdapterJson;

namespace AgentManager.Core.Agents;

/// <summary>
/// Antigravity SDK backend (API mode) for the "agy" engine. Drives the Antigravity Python SDK
/// through a thin stdio bridge process (<c>tools/am_agy_bridge.py</c>) so AM sees STRUCTURED
/// events (tool calls / thinking / streaming / brokered permissions) instead of the text-only
/// ConPTY transcript the subscription CLI (<see cref="AgyAdapter"/>) produces.
///
/// Protocol (mirrors PiAdapter): AM spawns <c>python am_agy_bridge.py</c> in the worktree, writes
/// ONE request JSON line on stdin, and parses JSONL events on stdout. Permissions round-trip back
/// on stdin via <see cref="BuildPermissionResponse"/>. The bridge runs ONE turn per process then
/// exits, so AM closes stdin on the first TurnCompleted.
///
/// API mode is the explicit billing choice: it authenticates to the Gemini Developer API
/// (GEMINI_API_KEY) and is billed per call (pay-as-you-go), unlike the subscription CLI. The host
/// (AppViewModel) selects this adapter for agy only when EngineAuthMode["agy"]=="api".
/// </summary>
public sealed class AgySdkAdapter : StdioJsonAdapter
{
    public override string Id => "agy";

    public override AgentCapabilities Capabilities { get; } = new(
        Permissions: true, Thinking: true, Sessions: true, Images: false, TokenUsage: true, Quota: false);

    // Keep stdin open: the bridge reads the request immediately, then optionally blocks for a
    // permission_decision line per brokered tool call. AM closes stdin on TurnCompleted.
    public override bool CloseStdinAfterStart => false;
    public override bool KillAfterTurnCompleted => true; // bridge should exit on its own, but ensure it

    /// <summary>Python interpreter to drive the bridge. AM resolves this (configured path else
    /// "python"/"py" on PATH) and passes it as <paramref name="executablePath"/>.</summary>
    public override ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = NewStdioStartInfo(executablePath, options.WorkingDirectory);
        psi.ArgumentList.Add(BridgeScriptPath); // resolved by the host to the bundled .py
        foreach (var ev in options.ExtraEnvironment) psi.Environment[ev.Key] = ev.Value; // GEMINI_API_KEY etc.
        return psi;
    }

    /// <summary>The bridge request line. Passed to AM via InitialStdinLines (written right after
    /// start), encoding everything the SDK needs for one turn.</summary>
    public override IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options)
    {
        // API key is NOT put on the request line; the host injects GEMINI_API_KEY via env
        // (ExtraEnvironment) so it never appears in the transcript/config traces.
        var req = new
        {
            prompt,
            cwd = options.WorkingDirectory,
            model = string.IsNullOrWhiteSpace(options.Model) ? null : options.Model,
            effort = options.ReasoningEffort,
            resume_id = options.ResumeSessionId,
            save_dir = Path.Combine(options.WorkingDirectory, ".am", "agy-sdk"),
            approvals = options.BypassPermissions ? "yolo" : "broker",
        };
        return [JsonSerializer.Serialize(req)];
    }

    /// <summary>Map one bridge JSONL line to normalized events. Resets the per-process turn flag.</summary>
    protected override IEnumerable<NormalizedEvent> ParseRoot(JsonElement root, string line)
    {
        switch (Str(root, "type"))
        {
            case "session_started":
            {
                yield return new SessionStarted(Str(root, "conversation_id") ?? "", null, 0, null);
                break;
            }
            case "assistant_delta":
            {
                var t = Str(root, "text");
                if (!string.IsNullOrEmpty(t)) yield return new AssistantDelta(t);
                break;
            }
            case "thinking":
            {
                var t = Str(root, "text");
                if (!string.IsNullOrEmpty(t)) yield return new Thinking(t);
                break;
            }
            case "tool_started":
            {
                yield return new ToolUseStarted(
                    Str(root, "id") ?? "",
                    Str(root, "name") ?? "",
                    Str(root, "input") ?? "{}");
                break;
            }
            case "tool_result":
            {
                yield return new ToolResult(
                    Str(root, "id") ?? "",
                    Str(root, "content") ?? "",
                    root.TryGetProperty("is_error", out var er) && er.ValueKind == JsonValueKind.True);
                break;
            }
            case "permission_request":
            {
                yield return new PermissionRequest(
                    Str(root, "id") ?? "",
                    Str(root, "name") ?? "",
                    Str(root, "args") ?? "{}",
                    Str(root, "id"));
                break;
            }
            case "token_usage":
            {
                yield return new TokenUsage(Lng(root, "in"), Lng(root, "out"));
                break;
            }
            case "engine_error":
            {
                var msg = Str(root, "message");
                if (!string.IsNullOrEmpty(msg)) yield return new EngineError(msg);
                break;
            }
            case "turn_completed":
            {
                var err = root.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                yield return new TurnCompleted(null, err, null, null);
                break;
            }
            default:
                yield return new RawUnknown(Str(root, "type") ?? "?", line);
                break;
        }
    }

    /// <summary>Approval round-trip: write a permission_decision line back on stdin so the bridge's
    /// blocking ask_user handler unblocks and returns Allow to the SDK.</summary>
    public override string? BuildPermissionResponse(PermissionRequest request, PermissionDecision decision)
    {
        var allow = decision.Allow;
        var obj = new { type = "permission_decision", id = request.RequestId, allow };
        return JsonSerializer.Serialize(obj);
    }

    /// <summary>Resolve the bundled bridge script. The host copies it next to the assembly at build
    /// time; fall back to a repo-relative <c>tools/</c> path for dev runs.</summary>
    public static string BridgeScriptPath { get; } = ResolveBridge();

    private static string ResolveBridge()
    {
        // 1) copied to output (ProductionToOutputDirectory / csproj CopyToOutput)
        var exeDir = AppContext.BaseDirectory;
        var nextToAssembly = Path.Combine(exeDir, "am_agy_bridge.py");
        if (File.Exists(nextToAssembly)) return nextToAssembly;
        // 2) dev: tools/ under the repo root (walk up from bin/.../net10.0-windows)
        var dir = exeDir;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var cand = Path.Combine(dir, "tools", "am_agy_bridge.py");
            if (File.Exists(cand)) return cand;
            dir = Path.GetDirectoryName(dir);
        }
        // 3) last resort: assume copied/copiable name relative to cwd at runtime
        return "am_agy_bridge.py";
    }
}
