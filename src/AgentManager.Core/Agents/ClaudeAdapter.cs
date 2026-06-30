using System.Diagnostics;
using System.Text.Json;
using AgentManager.Core.Events;
using static AgentManager.Core.Agents.AdapterJson;

namespace AgentManager.Core.Agents;

/// <summary>
/// Claude Code CLI adapter. Drives bidirectional stream-json over stdio.
/// Schema reference: docs/PHASE0_CLAUDE_STREAMJSON_KO.md (measured).
/// </summary>
public sealed class ClaudeAdapter : StdioJsonAdapter
{
    public override string Id => "claude";
    public override AgentCapabilities Capabilities { get; } = new(
        Permissions: true, Thinking: true, Sessions: true, Images: true, TokenUsage: true, Quota: true);
    public override bool CloseStdinAfterStart => false;

    /// <summary>The user message, held back until cc acks the init handshake. Sending init + a large user
    /// message in one stdin batch makes cc's stream-json reader stall (~90s on 60KB; a 19-byte message is
    /// 6s) — deferring the user message until after the init <c>control_response</c> keeps it fast (2s on
    /// the same 60KB), matching how an interactive terminal feeds the prompt only after startup. Per-turn
    /// state (a fresh adapter is created per turn).</summary>
    private string? _pendingUserMessage;

    public override ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = NewStdioStartInfo(executablePath, options.WorkingDirectory);
        psi.ArgumentList.Add("--output-format"); psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--input-format"); psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        AddNativeHookSettings(psi, options);
        if (options.Sandbox == SandboxMode.ReadOnly)
        {
            // No approval broker yet: read-only maps to plan mode (no edits/commands).
            psi.ArgumentList.Add("--permission-mode"); psi.ArgumentList.Add("plan");
        }
        else if (options.BypassPermissions)
            psi.ArgumentList.Add("--dangerously-skip-permissions");
        else { psi.ArgumentList.Add("--permission-prompt-tool"); psi.ArgumentList.Add("stdio"); }
        if (!string.IsNullOrWhiteSpace(options.Model)) { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(options.Model); }
        if (!string.IsNullOrWhiteSpace(options.ReasoningEffort) && options.ReasoningEffort != "default")
        { psi.ArgumentList.Add("--effort"); psi.ArgumentList.Add(options.ReasoningEffort); }
        if (!string.IsNullOrWhiteSpace(options.ResumeSessionId)) { psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(options.ResumeSessionId); }
        if (!string.IsNullOrWhiteSpace(options.McpConfigPath) && File.Exists(options.McpConfigPath))
        { psi.ArgumentList.Add("--mcp-config"); psi.ArgumentList.Add(options.McpConfigPath); }
        foreach (var dir in options.AdditionalDirectories)
            if (Directory.Exists(dir)) { psi.ArgumentList.Add("--add-dir"); psi.ArgumentList.Add(dir); }
        return psi;
    }

    private static void AddNativeHookSettings(ProcessStartInfo psi, SessionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NativeHookCommand)) return;
        var settings = JsonSerializer.Serialize(new
        {
            hooks = new Dictionary<string, object[]>
            {
                ["SubagentStart"] =
                [
                    new
                    {
                        matcher = "",
                        hooks = new object[] { new { type = "command", command = options.NativeHookCommand, timeout = 5 } }
                    }
                ],
                ["SubagentStop"] =
                [
                    new
                    {
                        matcher = "",
                        hooks = new object[] { new { type = "command", command = options.NativeHookCommand, timeout = 5 } }
                    }
                ]
            }
        });
        psi.ArgumentList.Add("--settings");
        psi.ArgumentList.Add(settings);
    }

    public override IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options)
    {
        var init = JsonSerializer.Serialize(new
        {
            type = "control_request",
            request_id = "init-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            request = new { subtype = "initialize" }
        });

        // text + attached images as base64 blocks (stream-json user message format)
        var content = new List<object> { new { type = "text", text = prompt } };
        foreach (var img in options.Images)
        {
            try
            {
                if (!File.Exists(img)) continue;
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = MediaTypeOf(img),
                        data = Convert.ToBase64String(File.ReadAllBytes(img)),
                    },
                });
            }
            catch { /* unreadable image — skip, keep the turn alive */ }
        }

        // Hold the user message; it is sent as a writeback once cc acks init (see ParseRoot). Sending it
        // in the same batch as init is the cause of the large-prompt stall.
        _pendingUserMessage = JsonSerializer.Serialize(new
        {
            type = "user",
            session_id = "",
            parent_tool_use_id = (string?)null,
            message = new { role = "user", content },
        });
        return [init];
    }

    /// <summary>Emit the deferred user message (once) now that init has been acknowledged.</summary>
    private IEnumerable<NormalizedEvent> FlushPendingUser()
    {
        if (_pendingUserMessage is { } user)
        {
            _pendingUserMessage = null;
            yield return new EngineWriteback(user);
        }
    }

    private static string MediaTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/png",
    };

    protected override IEnumerable<NormalizedEvent> ParseRoot(JsonElement root, string line)
    {
        var type = Str(root, "type");
        switch (type)
        {
            case "control_response":
                // cc's ack of our init control_request (the only control_response it emits). Init done →
                // send the deferred user message now; batching it with init is what stalls large prompts.
                foreach (var ev in FlushPendingUser()) yield return ev;
                break;

            case "system" when Str(root, "subtype") == "init":
                yield return new SessionStarted(
                    Str(root, "session_id") ?? "",
                    Str(root, "model"),
                    root.TryGetProperty("tools", out var t) && t.ValueKind == JsonValueKind.Array ? t.GetArrayLength() : 0,
                    Str(root, "cwd"));
                foreach (var ev in FlushPendingUser()) yield return ev; // fallback if no control_response arrived first
                break;

            case "rate_limit_event":
                if (root.TryGetProperty("rate_limit_info", out var rl))
                    // 구독 사용자의 rate_limit_event엔 utilization 필드가 없다(리셋 시각·타입만 옴).
                    // 실제 사용량 %는 /usage 명령으로만 나오므로, 없으면 -1(미상)로 전달한다.
                    yield return new QuotaUpdate(
                        rl.TryGetProperty("utilization", out var ut) && ut.ValueKind == JsonValueKind.Number ? ut.GetDouble() : -1,
                        Lng(rl, "resetsAt"),
                        Str(rl, "rateLimitType") ?? "", Str(rl, "status") ?? "");
                break;

            case "assistant":
                bool sub = root.TryGetProperty("parent_tool_use_id", out var p) && p.ValueKind == JsonValueKind.String;
                if (root.TryGetProperty("message", out var amsg))
                {
                    if (amsg.TryGetProperty("usage", out var u))
                        yield return new TokenUsage(
                            Lng(u, "input_tokens"), Lng(u, "output_tokens"),
                            Lng(u, "cache_read_input_tokens"), Lng(u, "cache_creation_input_tokens"));
                    if (amsg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        foreach (var b in content.EnumerateArray())
                        {
                            switch (Str(b, "type"))
                            {
                                case "text" when !string.IsNullOrWhiteSpace(Str(b, "text")):
                                    yield return new AssistantText(Str(b, "text")!.Trim(), sub); break;
                                case "thinking" when !string.IsNullOrWhiteSpace(Str(b, "thinking")):
                                    yield return new Thinking(Str(b, "thinking")!.Trim()); break;
                                case "tool_use":
                                    yield return new ToolUseStarted(
                                        Str(b, "id") ?? "", Str(b, "name") ?? "",
                                        b.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}");
                                    break;
                            }
                        }
                }
                break;

            case "user":
                bool usub = root.TryGetProperty("parent_tool_use_id", out var up) && up.ValueKind == JsonValueKind.String;
                if (root.TryGetProperty("message", out var umsg)
                    && umsg.TryGetProperty("content", out var ucontent) && ucontent.ValueKind == JsonValueKind.Array)
                    foreach (var b in ucontent.EnumerateArray())
                        if (Str(b, "type") == "tool_result")
                            yield return new ToolResult(
                                Str(b, "tool_use_id") ?? "",
                                b.TryGetProperty("content", out var c) ? (c.ValueKind == JsonValueKind.String ? c.GetString()! : c.GetRawText()) : "",
                                b.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True,
                                usub);
                break;

            case "control_request" when root.TryGetProperty("request", out var req) && Str(req, "subtype") == "can_use_tool":
                yield return new PermissionRequest(
                    Str(root, "request_id") ?? "",
                    Str(req, "tool_name") ?? "",
                    req.TryGetProperty("input", out var pinp) ? pinp.GetRawText() : "{}",
                    Str(req, "tool_use_id"));
                break;

            case "result":
                // result.usage is the authoritative turn total (per-message usage undercounts output).
                TokenUsage? turnUsage = null;
                if (root.TryGetProperty("usage", out var ru) && ru.ValueKind == JsonValueKind.Object)
                    turnUsage = new TokenUsage(
                        Lng(ru, "input_tokens"), Lng(ru, "output_tokens"),
                        Lng(ru, "cache_read_input_tokens"), Lng(ru, "cache_creation_input_tokens"));
                yield return new TurnCompleted(
                    Str(root, "result"),
                    root.TryGetProperty("is_error", out var re) && re.ValueKind == JsonValueKind.True,
                    root.TryGetProperty("total_cost_usd", out var cost) && cost.ValueKind == JsonValueKind.Number ? cost.GetDouble() : null,
                    root.TryGetProperty("num_turns", out var nt) && nt.ValueKind == JsonValueKind.Number ? nt.GetInt32() : null,
                    turnUsage);
                break;

            default:
                yield return new RawUnknown(type ?? "?", line);
                break;
        }
    }

    /// <summary>control_response per the measured stdio permission protocol (Phase 0 capture):
    /// allow echoes updatedInput + toolUseID; deny carries a message and interrupts.</summary>
    public override string? BuildPermissionResponse(Events.PermissionRequest request, PermissionDecision decision)
    {
        object inner;
        if (decision.Allow)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(request.InputJson) ? "{}" : request.InputJson);
            inner = new
            {
                behavior = "allow",
                updatedInput = doc.RootElement.Clone(),
                toolUseID = request.ToolUseId,
            };
        }
        else
        {
            inner = new
            {
                behavior = "deny",
                message = decision.Reason ?? "User denied permission",
                interrupt = true,
            };
        }
        return JsonSerializer.Serialize(new
        {
            type = "control_response",
            response = new { subtype = "success", request_id = request.RequestId, response = inner },
        });
    }

}
