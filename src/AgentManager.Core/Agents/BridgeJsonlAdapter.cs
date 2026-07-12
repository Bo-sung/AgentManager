using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentManager.Core.Events;
using static AgentManager.Core.Agents.AdapterJson;

namespace AgentManager.Core.Agents;

/// <summary>
/// Adapter for the <b>"agentmanager-bridge-jsonl"</b> custom-engine protocol (protocolVersion 1) —
/// a richer line-delimited-JSON transport than <see cref="OneShotTextAdapter"/> that lets a third-party CLI
/// expose full tool-call / thinking / token-usage visibility. Spec: <c>docs/BRIDGE_JSONL_PROTOCOL_KO.md</c>.
///
/// Transport: child process, UTF-8 no BOM, one JSON object per line on stdout, each with a string <c>type</c>.
/// Unknown types → <see cref="RawUnknown"/> (forward-compat). Blank/malformed lines are skipped by the
/// <see cref="StdioJsonAdapter"/> base. stderr → EngineError via AgentSession's stderr pump.
///
/// Prompt delivery — auto-selected from the launch-args template:
///   • ARGS mode (args contain <c>{prompt}</c>): the prompt is substituted onto argv and stdin is CLOSED
///     after start (avoids a codex-style stdin hang). Placeholders: {prompt} {model} {cwd} {sessionId}.
///   • STDIN mode (no <c>{prompt}</c> in args): stdin stays OPEN and one <c>start</c> line is written;
///     suits a persistent/server CLI, so the process is reaped after turn_completed
///     (<see cref="KillAfterTurnCompleted"/>, mirrors <see cref="PiAdapter"/>).
///
/// Completion: <c>turn_completed</c> is the normal boundary (guarded against duplicates). If the process
/// exits without it, AgentSession synthesizes a TurnCompleted (non-zero exit → error) so a silent/crashed
/// bridge never hangs.
/// </summary>
public sealed class BridgeJsonlAdapter : StdioJsonAdapter
{
    private readonly string _engineId;
    private readonly IReadOnlyList<string> _args;
    private readonly bool _stdinMode;   // no {prompt} in args → deliver the prompt via a stdin start line
    private bool _started;
    private bool _completed;

    public BridgeJsonlAdapter(string engineId, IReadOnlyList<string> argsTemplate)
    {
        _engineId = engineId;
        _args = argsTemplate ?? [];
        _stdinMode = !_args.Any(a => a.Contains("{prompt}"));
    }

    public override string Id => _engineId;

    public override AgentCapabilities Capabilities { get; } = new(
        Permissions: false, Thinking: true, Sessions: true, Images: false, TokenUsage: true, Quota: false);

    public override bool CloseStdinAfterStart => !_stdinMode;   // args mode: prompt is in argv → close stdin (no hang)
    public override bool KillAfterTurnCompleted => _stdinMode;  // stdin/server mode: reap after the turn boundary

    public override ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = NewStdioStartInfo(executablePath, options.WorkingDirectory);
        foreach (var arg in _args)
            psi.ArgumentList.Add(Substitute(arg, prompt, options));
        foreach (var ev in options.ExtraEnvironment) psi.Environment[ev.Key] = ev.Value; // provider API keys etc.
        return psi;
    }

    public override IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options)
        => _stdinMode
            ? [JsonSerializer.Serialize(new
            {
                type = "start",
                prompt,
                model = options.Model ?? "",
                cwd = options.WorkingDirectory,
                sessionId = options.ResumeSessionId ?? "",
            })]
            : [];

    protected override IEnumerable<NormalizedEvent> ParseRoot(JsonElement root, string line)
    {
        switch (Str(root, "type"))
        {
            case "session_started":
                _started = true;
                yield return new SessionStarted(
                    Str(root, "sessionId") ?? "",
                    Str(root, "model"),
                    root.TryGetProperty("toolCount", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0,
                    Str(root, "cwd"));
                break;

            // Streaming fragments: shown immediately, NOT translated (same class as pi deltas).
            case "assistant_delta" when Str(root, "text") is { Length: > 0 } d:
                foreach (var e in EnsureStarted()) yield return e; // synth SessionStarted if the CLI omitted it
                yield return new AssistantDelta(d);
                break;

            // Final full message: EN→KO translation target, replaces accumulated deltas.
            case "assistant_text" when Str(root, "text") is { Length: > 0 } t:
                yield return new AssistantText(t.Trim());
                break;

            // Emit once per block (not per token) to avoid line flooding — same convention as ClaudeAdapter/PiAdapter.
            case "thinking" when Str(root, "text") is { Length: > 0 } th:
                yield return new Thinking(th.Trim());
                break;

            case "tool_started":
                yield return new ToolUseStarted(Str(root, "id") ?? "", Str(root, "name") ?? "", InputJson(root));
                break;

            case "tool_result":
                yield return new ToolResult(
                    Str(root, "id") ?? "",
                    ContentText(root),
                    root.TryGetProperty("isError", out var er) && er.ValueKind == JsonValueKind.True);
                break;

            case "token_usage":
                yield return UsageOf(root);
                break;

            case "error":
                yield return new EngineError(Str(root, "message") ?? "bridge error");
                break;

            case "turn_completed":
                if (_completed) break; // dup-completion guard (a second terminal line is ignored)
                _completed = true;
                TokenUsage? usage = root.TryGetProperty("usage", out var uu) && uu.ValueKind == JsonValueKind.Object
                    ? UsageOf(uu) : null;
                yield return new TurnCompleted(
                    Str(root, "text"),
                    root.TryGetProperty("isError", out var ie) && ie.ValueKind == JsonValueKind.True,
                    root.TryGetProperty("costUsd", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null,
                    root.TryGetProperty("numTurns", out var nt) && nt.ValueKind == JsonValueKind.Number ? nt.GetInt32() : null,
                    usage);
                break;

            default:
                yield return new RawUnknown(Str(root, "type") ?? "?", line);
                break;
        }
    }

    /// <summary>Synthesize a minimal SessionStarted (engineId only) if the CLI streams before session_started —
    /// kept minimal so a real session id arriving later is what the UI ultimately shows.</summary>
    private IEnumerable<NormalizedEvent> EnsureStarted()
    {
        if (!_started) { _started = true; yield return new SessionStarted(_engineId, null, 0, null); }
    }

    private static TokenUsage UsageOf(JsonElement u)
        => new(Lng(u, "input"), Lng(u, "output"), Lng(u, "cacheRead"), Lng(u, "cacheWrite"), Lng(u, "reasoning"));

    /// <summary>tool input: a JSON object (raw text) or a string (wrapped as {"text":..}); missing → "{}".</summary>
    private static string InputJson(JsonElement root)
    {
        if (!root.TryGetProperty("input", out var i)) return "{}";
        return i.ValueKind == JsonValueKind.String
            ? JsonSerializer.Serialize(new { text = i.GetString() })
            : i.GetRawText();
    }

    /// <summary>tool result content: a string, a content-array [{type:text,text}] (flattened), or raw JSON.</summary>
    private static string ContentText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var b in c.EnumerateArray())
                if (Str(b, "type") == "text" && Str(b, "text") is { } tx) sb.Append(tx);
            return sb.ToString();
        }
        return c.GetRawText();
    }

    private static string Substitute(string arg, string prompt, SessionOptions options) => arg
        .Replace("{prompt}", prompt)
        .Replace("{model}", options.Model ?? "")
        .Replace("{cwd}", options.WorkingDirectory)
        .Replace("{sessionId}", options.ResumeSessionId ?? "");
}
