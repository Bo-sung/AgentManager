using System.Diagnostics;
using System.Text.Json;
using AgentManager.Core.Events;
using static AgentManager.Core.Agents.AdapterJson;

namespace AgentManager.Core.Agents;

/// <summary>
/// Agent Client Protocol (ACP) adapter — Zed's newline-delimited JSON-RPC-2.0-over-stdio protocol.
/// Used by custom engines that expose an ACP server (verified: opencode <c>opencode acp</c> v1.17.18,
/// hermes <c>hermes-acp</c> v0.18.2 — both protocolVersion 1). The launch subcommand comes from the
/// engine manifest's <c>launch.args</c> (e.g. <c>["acp"]</c> for opencode, <c>[]</c> for hermes-acp).
///
/// Like <see cref="CodexAppServerAdapter"/> this is a STATEFUL handshake driven via <see cref="EngineWriteback"/>:
/// initialize → (response) session/new → (response) capture sessionId + session/prompt → stream
/// session/update notifications → the session/prompt response carries {stopReason, usage} = turn done.
/// Agent→client requests (session/request_permission) route through the PermissionRequest/BuildPermissionResponse
/// flow; any other agent request is answered with a JSON-RPC error so the agent never hangs.
/// We advertise <c>fs</c> capability = false so the agent uses its OWN file tools (surfaced as tool_call),
/// avoiding a client-side filesystem handler. Instance is single-use per turn (holds state) — never reuse.
/// </summary>
public sealed class AcpAdapter : StdioJsonAdapter
{
    private readonly string _engineId;
    private readonly IReadOnlyList<string> _argsTemplate;

    public AcpAdapter(string engineId, IReadOnlyList<string> argsTemplate)
    {
        _engineId = string.IsNullOrWhiteSpace(engineId) ? "acp" : engineId;
        _argsTemplate = argsTemplate ?? [];
    }

    public override string Id => _engineId;
    public override AgentCapabilities Capabilities { get; } = new(
        Permissions: true, Thinking: true, Sessions: true, Images: false, TokenUsage: true, Quota: false);
    public override bool CloseStdinAfterStart => false;    // keep stdin open for the sequenced handshake writebacks
    public override bool KillAfterTurnCompleted => true;   // ACP agent is a long-lived server — stop it after the turn

    private const int InitializeId = 1;
    private const int SessionNewId = 2;
    private const int PromptId = 3;

    private SessionOptions _options = null!;
    private string _prompt = "";
    private string? _sessionId;
    private bool _completed;

    public override ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = NewStdioStartInfo(executablePath, options.WorkingDirectory);
        foreach (var a in _argsTemplate)
            psi.ArgumentList.Add(Substitute(a, options));   // usually just the ACP subcommand; {cwd} etc. optional
        return psi;
    }

    private static string Substitute(string arg, SessionOptions o) => arg
        .Replace("{cwd}", o.WorkingDirectory)
        .Replace("{model}", o.Model ?? "");

    public override IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options)
    {
        _options = options;
        _prompt = prompt;
        // fs=false ⇒ the agent uses its own read/write tools (visible as tool_call) — no client fs handler needed.
        return
        [
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = InitializeId,
                method = "initialize",
                @params = new
                {
                    protocolVersion = 1,
                    clientCapabilities = new { fs = new { readTextFile = false, writeTextFile = false } },
                },
            }),
        ];
    }

    protected override IEnumerable<NormalizedEvent> ParseRoot(JsonElement root, string line)
    {
        var method = Str(root, "method");
        var hasId = root.TryGetProperty("id", out var idEl);
        var isNumId = hasId && idEl.ValueKind == JsonValueKind.Number;

        // ---- responses to OUR requests (method absent, numeric id) — advance the handshake ----
        if (method is null && isNumId)
        {
            var id = idEl.GetInt32();
            if (root.TryGetProperty("error", out var err))
            {
                yield return new EngineError($"acp request {id} failed: {err.GetRawText()}");
                // Any handshake/prompt error ends the turn so the session doesn't hang.
                if (!_completed) { _completed = true; yield return new TurnCompleted(null, true, null, null); }
                yield break;
            }
            var result = root.TryGetProperty("result", out var r) ? r : default;
            switch (id)
            {
                case InitializeId:
                    yield return new EngineWriteback(BuildSessionNew());
                    break;
                case SessionNewId:
                    _sessionId = result.ValueKind == JsonValueKind.Object ? Str(result, "sessionId") : null;
                    if (string.IsNullOrEmpty(_sessionId))
                    {
                        yield return new EngineError("acp: sessionId missing in session/new response");
                        if (!_completed) { _completed = true; yield return new TurnCompleted(null, true, null, null); }
                        break;
                    }
                    yield return new SessionStarted(_sessionId!, _options.Model, 0, _options.WorkingDirectory);
                    yield return new EngineWriteback(BuildPrompt());
                    break;
                case PromptId:
                    // session/prompt result = { stopReason, usage? } → the turn is complete.
                    var stop = result.ValueKind == JsonValueKind.Object ? Str(result, "stopReason") : null;
                    var usage = UsageOf(result);
                    var isErr = stop is "refusal";
                    if (!_completed) { _completed = true; yield return new TurnCompleted(null, isErr, null, null, usage); }
                    break;
            }
            yield break;
        }

        // ---- agent → client REQUESTS (method + id; a response is mandatory or the agent hangs) ----
        if (method is not null && hasId)
        {
            if (method == "session/request_permission")
            {
                var p = root.TryGetProperty("params", out var prm) ? prm.GetRawText() : "{}";
                var toolCallId = root.TryGetProperty("params", out var prm2) && prm2.TryGetProperty("toolCall", out var tcp)
                    ? Str(tcp, "toolCallId") : null;
                var toolName = root.TryGetProperty("params", out var prm3) && prm3.TryGetProperty("toolCall", out var tc)
                    ? (Str(tc, "title") ?? Str(tc, "kind") ?? "tool") : "tool";
                yield return new PermissionRequest(idEl.GetRawText(), toolName, p, toolCallId);
            }
            else
            {
                // Unsupported agent request (fs/* shouldn't come since we advertised fs=false; terminal/* etc.):
                // answer with a JSON-RPC error so the agent doesn't block forever.
                yield return new EngineWriteback(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = isNumId ? (object)idEl.GetInt32() : idEl.GetRawText(),
                    error = new { code = -32601, message = "unsupported by AgentManager" },
                }));
            }
            yield break;
        }

        // ---- notifications (method, no id) ----
        if (method != "session/update")
        {
            // session/cancel echo, or agent-side notifications we don't model.
            if (method is not null) yield return new RawUnknown(method, line);
            yield break;
        }

        var ps = root.TryGetProperty("params", out var pEl) ? pEl : default;
        var u = ps.ValueKind == JsonValueKind.Object && ps.TryGetProperty("update", out var uEl) ? uEl : default;
        switch (u.ValueKind == JsonValueKind.Object ? Str(u, "sessionUpdate") : null)
        {
            case "agent_message_chunk":
                if (TextOf(u) is { Length: > 0 } msg) yield return new AssistantDelta(msg);
                break;

            case "agent_thought_chunk":
                if (TextOf(u) is { Length: > 0 } th) yield return new Thinking(th);
                break;

            case "tool_call":
                yield return new ToolUseStarted(
                    Str(u, "toolCallId") ?? "",
                    Str(u, "title") ?? Str(u, "kind") ?? "tool",
                    u.TryGetProperty("rawInput", out var ri) ? ri.GetRawText() : "{}");
                break;

            case "tool_call_update":
                var status = Str(u, "status");
                if (status is "completed" or "failed")
                    yield return new ToolResult(Str(u, "toolCallId") ?? "", ToolContentOf(u), status == "failed");
                break;

            // Context/session housekeeping — not turn output.
            case "usage_update" or "plan" or "available_commands_update"
                or "current_mode_update" or "user_message_chunk":
                break;

            default:
                yield return new RawUnknown("session/update", line);
                break;
        }
    }

    /// <summary>ACP permission answer: select the allow/reject option the agent offered (params.options[]),
    /// matching on the option kind. Falls back to "cancelled" when no suitable option is present.</summary>
    public override string? BuildPermissionResponse(PermissionRequest request, PermissionDecision decision)
    {
        string? optionId = null;
        try
        {
            using var doc = JsonDocument.Parse(request.InputJson);
            if (doc.RootElement.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                // Prefer an "always"/for-session option when the user chose ForSession, else the once variant.
                string[] allow = decision.ForSession
                    ? ["allow_always", "allow_once"] : ["allow_once", "allow_always"];
                string[] reject = decision.ForSession
                    ? ["reject_always", "reject_once"] : ["reject_once", "reject_always"];
                optionId = PickByKind(opts, decision.Allow ? allow : reject) ?? FirstOptionId(opts);
            }
        }
        catch { /* malformed params → fall through to cancelled */ }

        object idPart = int.TryParse(request.RequestId, out var n) ? n : request.RequestId;
        object outcome = decision.Allow && optionId is not null
            ? new { outcome = "selected", optionId }
            : (object)new { outcome = "cancelled" };
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id = idPart, result = new { outcome } });
    }

    // ---- handshake payloads ----

    private string BuildSessionNew() => JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = SessionNewId,
        method = string.IsNullOrWhiteSpace(_options.ResumeSessionId) ? "session/new" : "session/load",
        @params = string.IsNullOrWhiteSpace(_options.ResumeSessionId)
            ? (object)new { cwd = _options.WorkingDirectory, mcpServers = Array.Empty<object>() }
            : new { cwd = _options.WorkingDirectory, mcpServers = Array.Empty<object>(), sessionId = _options.ResumeSessionId },
    });

    private string BuildPrompt() => JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = PromptId,
        method = "session/prompt",
        @params = new { sessionId = _sessionId, prompt = new object[] { new { type = "text", text = _prompt } } },
    });

    // ---- helpers ----

    /// <summary>Text of an update's content block ({type:"text",text}), whether content is an object or array.</summary>
    private static string? TextOf(JsonElement update)
    {
        if (!update.TryGetProperty("content", out var c)) return null;
        if (c.ValueKind == JsonValueKind.Object)
            return Str(c, "type") == "text" ? Str(c, "text") : null;
        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in c.EnumerateArray())
                if (Str(b, "type") == "text" && Str(b, "text") is { } t) sb.Append(t);
            return sb.Length == 0 ? null : sb.ToString();
        }
        return null;
    }

    /// <summary>Flatten a tool_call_update's content[] (each {type:"content",content:{type:"text",text}}) to text.</summary>
    private static string ToolContentOf(JsonElement update)
    {
        if (!update.TryGetProperty("content", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return update.TryGetProperty("rawOutput", out var ro) ? ro.GetRawText() : "";
        var sb = new System.Text.StringBuilder();
        foreach (var block in arr.EnumerateArray())
        {
            var inner = block.TryGetProperty("content", out var ic) ? ic : block;
            if (Str(inner, "type") == "text" && Str(inner, "text") is { } t) sb.Append(t);
            else sb.Append(block.GetRawText());
        }
        return sb.ToString();
    }

    private static TokenUsage? UsageOf(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("usage", out var us) || us.ValueKind != JsonValueKind.Object)
            return null;
        return new TokenUsage(Lng(us, "inputTokens"), Lng(us, "outputTokens"), CacheReadTokens: Lng(us, "cachedReadTokens"));
    }

    private static string? PickByKind(JsonElement options, string[] kinds)
    {
        foreach (var kind in kinds)
            foreach (var o in options.EnumerateArray())
                if (Str(o, "kind") == kind) return Str(o, "optionId");
        return null;
    }

    private static string? FirstOptionId(JsonElement options)
    {
        foreach (var o in options.EnumerateArray()) return Str(o, "optionId");
        return null;
    }
}
