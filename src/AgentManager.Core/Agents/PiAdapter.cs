using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AgentManager.Core.Events;
using static AgentManager.Core.Agents.AdapterJson;

namespace AgentManager.Core.Agents;

/// <summary>
/// pi (pi.dev) RPC 모드 어댑터. 실측: docs/PHASE0_PI_RPC_KO.md + pi 0.80.3 dist/core·modes/rpc 타입.
///
/// General/Main pi는 <c>node dist/cli.js --mode rpc</c>, Worker pi는 pi-worker의
/// <c>node dist/cli/index.js --mode rpc</c>로 구동한다(같은 엔진/어댑터 — 실행 파일만 다름).
/// 핸드셰이크 없음: 시작 시 stdin으로 get_state(세션 id 확보) + prompt 를 보내고,
/// stdout JSONL 이벤트를 정규화한다. RPC 서버는 턴 후에도 살아있으므로
/// 완료 시 AgentSession이 종료(<see cref="KillAfterTurnCompleted"/>).
///
/// 완료 판정: <c>agent_end</c>는 시도마다 발생하며 <c>willRetry</c>가 true면 auto-retry가
/// 이어지므로, willRetry:false일 때만 <see cref="TurnCompleted"/>를 낸다(pi 0.80.3).
/// extension_ui_request: blocking(select/confirm/input/editor)은 즉시 cancel 응답(무기한 대기 차단),
/// 나머지는 fire-and-forget이라 무시.
/// </summary>
public sealed class PiAdapter : StdioJsonAdapter
{
    public override string Id => "pi";

    public override AgentCapabilities Capabilities { get; } = new(
        Permissions: false, Thinking: true, Sessions: true, Images: true, TokenUsage: true, Quota: false);

    public override bool CloseStdinAfterStart => false;     // prompt를 stdin으로 보내고 유지
    public override bool KillAfterTurnCompleted => true;     // RPC 서버는 안 죽으므로 agent_end 후 종료

    private TokenUsage? _lastUsage;
    private bool _turnErrored;

    public override ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        // executablePath = EngineRegistry가 해석한 실행 대상:
        //   General/Main pi → pi의 dist/cli.js,  Worker pi → pi-worker의 dist/cli/index.js.
        // 둘 다 node 스크립트(.js)라 `node <path>`로 구동. pi-worker를 실제 실행파일/shim(.exe 등)으로
        // 오버라이드한 경우에만 직접 실행한다(하네스 계약: `pi-worker --mode rpc`, argv는 공식 pi로 pass-through).
        var viaNode = executablePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
        var psi = NewStdioStartInfo(viaNode ? "node" : executablePath, options.WorkingDirectory);
        if (viaNode) psi.ArgumentList.Add(executablePath);
        psi.ArgumentList.Add("--mode"); psi.ArgumentList.Add("rpc");
        // 모델은 "provider/id[:thinking]" — ~/.pi 기본값을 덮으려면 명시해야 함(PHASE0 §5).
        if (!string.IsNullOrWhiteSpace(options.Model) && options.Model != "default")
        { psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(options.Model); }
        if (!string.IsNullOrWhiteSpace(options.ReasoningEffort) && options.ReasoningEffort != "default")
        { psi.ArgumentList.Add("--thinking"); psi.ArgumentList.Add(options.ReasoningEffort); }
        if (!string.IsNullOrWhiteSpace(options.ResumeSessionId))
        { psi.ArgumentList.Add("--session"); psi.ArgumentList.Add(options.ResumeSessionId!); }
        foreach (var ev in options.ExtraEnvironment) psi.Environment[ev.Key] = ev.Value; // provider API 키 등
        return psi;
    }

    public override IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options)
    {
        // 1) 세션 id 확보용 get_state  2) 실제 prompt(이미지 포함)
        var getState = JsonSerializer.Serialize(new { type = "get_state" });
        var promptCmd = JsonSerializer.Serialize(new { type = "prompt", message = prompt, images = ImagesPayload(options.Images) });
        return [getState, promptCmd];
    }

    private static object[] ImagesPayload(IReadOnlyList<string> images)
    {
        var list = new List<object>();
        foreach (var img in images)
        {
            try
            {
                if (!File.Exists(img)) continue;
                list.Add(new { type = "image", data = Convert.ToBase64String(File.ReadAllBytes(img)), mimeType = MimeOf(img) });
            }
            catch { /* 읽기 실패 이미지는 건너뛴다 */ }
        }
        return [.. list];
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/png",
    };

    protected override IEnumerable<NormalizedEvent> ParseRoot(JsonElement root, string line)
    {
        switch (Str(root, "type"))
        {
            case "response":
            {
                var success = root.TryGetProperty("success", out var sc) && sc.ValueKind == JsonValueKind.True;
                if (!success) { yield return new EngineError(Str(root, "error") ?? "pi command failed"); break; }
                // get_state 응답 → 세션 id/모델 확보 (pi엔 별도 session-started 이벤트가 없음)
                if (Str(root, "command") == "get_state" && root.TryGetProperty("data", out var st))
                {
                    var model = st.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.Object ? Str(m, "id") : null;
                    yield return new SessionStarted(Str(st, "sessionId") ?? "", model, 0, null);
                }
                break;
            }

            case "message_update":
                if (root.TryGetProperty("assistantMessageEvent", out var ev))
                    switch (Str(ev, "type"))
                    {
                        // 텍스트는 스트리밍 델타(UI가 누적). thinking은 델타 누적용 이벤트가 없어
                        // thinking_end에서 전체를 한 번만 내보낸다(cc/gx와 동일 — 줄 폭주 방지).
                        case "text_delta" when Str(ev, "delta") is { Length: > 0 } d:
                            yield return new AssistantDelta(d); break;
                        case "thinking_end" when Str(ev, "content") is { Length: > 0 } tk:
                            yield return new Thinking(tk.Trim()); break;
                    }
                break;

            case "tool_execution_start":
                yield return new ToolUseStarted(
                    Str(root, "toolCallId") ?? "",
                    Str(root, "toolName") ?? "",
                    root.TryGetProperty("args", out var a) ? a.GetRawText() : "{}");
                break;

            case "tool_execution_end":
                yield return new ToolResult(
                    Str(root, "toolCallId") ?? "",
                    root.TryGetProperty("result", out var r) ? ContentText(r) : "",
                    root.TryGetProperty("isError", out var er) && er.ValueKind == JsonValueKind.True);
                break;

            case "message_end":
                if (root.TryGetProperty("message", out var msg) && Str(msg, "role") == "assistant")
                {
                    if (msg.TryGetProperty("usage", out var u))
                        _lastUsage = new TokenUsage(Lng(u, "input"), Lng(u, "output"), Lng(u, "cacheRead"), Lng(u, "cacheWrite"));
                    if (Str(msg, "stopReason") == "error")
                    {
                        _turnErrored = true;
                        yield return new EngineError(Str(msg, "errorMessage") ?? "pi turn error");
                    }
                    else
                    {
                        // A successful assistant step clears a prior (retryable) error so a recovered
                        // auto-retry is reported as a completed turn — not a failure — at the final
                        // agent_end. Mirrors pi's own last-assistant-message check in _willRetryAfterAgentEnd.
                        _turnErrored = false;
                        if (AssistantTextOf(msg) is { Length: > 0 } text)
                            yield return new AssistantText(text.Trim());
                    }
                }
                break;

            case "agent_end":
                // pi 0.80.3: agent_end fires once PER attempt and carries willRetry (auto-retry pending —
                // core/agent-session.d.ts, _willRetryAfterAgentEnd = retryEnabled && attempt<max &&
                // last-assistant-is-retryable-error). Completing on a willRetry:true agent_end would let
                // AgentSession kill the process mid-retry. Only willRetry:false is the real turn boundary;
                // it always arrives eventually (the retry counter is bounded by maxRetries).
                if (root.TryGetProperty("willRetry", out var willRetry) && willRetry.ValueKind == JsonValueKind.True)
                    break; // retry pending — keep the process alive for the next attempt
                yield return new TurnCompleted(null, _turnErrored, null, null, _lastUsage);
                break;

            case "extension_error":
                yield return new EngineError(Str(root, "error") ?? "pi extension error");
                break;

            case "extension_ui_request":
            {
                // pi 0.80.3 rpc-mode: select/confirm/input/editor BLOCK the turn until an
                // extension_ui_response with the matching id arrives (dist/modes/rpc/rpc-types.d.ts +
                // rpc-mode.js pendingExtensionRequests). A worker is headless (no user to answer) and
                // pi's interactive dialog UI is not wired (Capabilities.Permissions=false), so an
                // unanswered blocking request hangs the turn forever. Policy: cancel blocking requests
                // IMMEDIATELY — the extension then resolves to its safe default (undefined/false, i.e.
                // deny/no-op). Cancelling on receipt means nothing is ever left pending, so abort/exit
                // cleanup is a no-op. notify/setStatus/setWidget/setTitle/set_editor_text are
                // fire-and-forget (rpc-mode "no response needed") → ignore.
                var method = Str(root, "method");
                if (method is "select" or "confirm" or "input" or "editor"
                    && Str(root, "id") is { Length: > 0 } reqId)
                    yield return new EngineWriteback(
                        JsonSerializer.Serialize(new { type = "extension_ui_response", id = reqId, cancelled = true }));
                break;
            }

            // 무시: agent_start, turn_start, turn_end, message_start, tool_execution_update,
            //       queue_update(steering/follow-up는 우리 단일턴 모델에서 미사용), compaction_*, auto_retry_*
            case "agent_start" or "turn_start" or "turn_end" or "message_start"
                or "tool_execution_update" or "queue_update"
                or "compaction_start" or "compaction_end"
                or "auto_retry_start" or "auto_retry_end":
                break;

            default:
                yield return new RawUnknown(Str(root, "type") ?? "?", line);
                break;
        }
    }

    /// <summary>assistant 메시지 content 배열에서 text 블록만 이어붙인다.</summary>
    private static string AssistantTextOf(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        foreach (var b in content.EnumerateArray())
            if (Str(b, "type") == "text" && Str(b, "text") is { } t) sb.Append(t);
        return sb.ToString();
    }

    /// <summary>tool 결과 content([{type:text,text}]) → 평문, 없으면 원문 JSON.</summary>
    private static string ContentText(JsonElement result)
    {
        if (result.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var b in c.EnumerateArray())
                if (Str(b, "type") == "text" && Str(b, "text") is { } t) sb.Append(t);
            return sb.ToString();
        }
        return result.GetRawText();
    }
}
