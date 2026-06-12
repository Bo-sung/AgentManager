using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentManager.Core.Events;

namespace AgentManager.Core.Agents;

/// <summary>
/// Codex app-server 어댑터 (승인 Stage 2). 개행 구분 JSON-RPC over stdio.
/// 실측: docs/PHASE0_CODEX_APPSERVER_KO.md (Smoke --appserver-probe PASS).
///
/// exec --json과 달리 상태형 핸드셰이크가 필요해서 ParseLine이 <see cref="EngineWriteback"/>으로
/// 다음 요청을 내보낸다: initialize 응답 → initialized + thread/start(or resume) → 응답에서 threadId
/// 확보 → turn/start. 승인은 서버→클라 JSON-RPC 요청(item/*/requestApproval)으로 오고, 기존
/// PermissionRequest/PermissionDecision 흐름이 {"id":N,"result":{"decision":...}}로 응답한다.
///
/// 정책: 이 어댑터는 RequireApproval=true 전용 — sandbox는 danger-full-access로 두고
/// approvalPolicy=untrusted가 게이트 역할 (윈도우 샌드박스 spawn 불가 실측 + Claude Stage 1과 동일 정책).
/// 인스턴스는 턴(프로세스) 1회용 — 상태를 가지므로 재사용 금지.
/// </summary>
public sealed class CodexAppServerAdapter : IAgentAdapter
{
    public string Id => "codex-appserver";
    public AgentCapabilities Capabilities { get; } = new(
        Permissions: true, Thinking: true, Sessions: true, Images: true, TokenUsage: true, Quota: true);
    public bool CloseStdinAfterStart => false;
    public bool KillAfterTurnCompleted => true;

    private const int InitializeId = 1;
    private const int ThreadId = 2;
    private const int TurnId = 3;

    private SessionOptions _options = null!;
    private string _prompt = "";
    private string? _threadId;
    private TokenUsage? _lastUsage;
    private bool _turnFailed;

    public ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            StandardInputEncoding = Utf8NoBom,
        };
        psi.ArgumentList.Add("app-server");
        return psi;
    }

    public IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options)
    {
        _options = options;
        _prompt = prompt;
        return
        [
            JsonSerializer.Serialize(new
            {
                id = InitializeId,
                method = "initialize",
                @params = new { clientInfo = new { name = "AgentManager", title = "AgentManager", version = "0.1.0" } },
            }),
        ];
    }

    public IEnumerable<NormalizedEvent> ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;
        JsonElement root;
        try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
        catch { yield break; }

        var method = Str(root, "method");
        var hasId = root.TryGetProperty("id", out var idEl);

        // ---- 우리 요청에 대한 응답 (핸드셰이크 진행) ----
        if (method is null && hasId && idEl.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetProperty("error", out var err))
            {
                yield return new EngineError($"app-server request {idEl.GetInt32()} failed: {err.GetRawText()}");
                yield break;
            }
            switch (idEl.GetInt32())
            {
                case InitializeId:
                    yield return new EngineWriteback(JsonSerializer.Serialize(new { method = "initialized" }));
                    yield return new EngineWriteback(BuildThreadRequest());
                    break;
                case ThreadId:
                    _threadId = FindThreadId(root);
                    if (_threadId is null)
                    {
                        yield return new EngineError("app-server: thread id missing in thread/start response");
                        break;
                    }
                    yield return new SessionStarted(_threadId, _options.Model, 0, _options.WorkingDirectory);
                    yield return new EngineWriteback(BuildTurnStart());
                    break;
                // TurnId 응답은 수락 통보일 뿐 — 진행은 알림으로 온다
            }
            yield break;
        }

        // ---- 서버 → 클라이언트 요청 (승인 등; 응답 필수) ----
        if (method is not null && hasId)
        {
            if (method.Contains("requestApproval", StringComparison.Ordinal)
                || method is "execCommandApproval" or "applyPatchApproval")
            {
                var p = root.TryGetProperty("params", out var prm) ? prm.GetRawText() : "{}";
                var itemId = root.TryGetProperty("params", out var prm2) ? Str(prm2, "itemId") : null;
                yield return new PermissionRequest(idEl.GetRawText(), ToolNameOf(method), p, itemId);
            }
            else
            {
                // 미지원 서버 요청(requestUserInput/elicitation 등): 막히지 않게 에러로 응답
                yield return new EngineWriteback(JsonSerializer.Serialize(new
                {
                    id = idEl.ValueKind == JsonValueKind.Number ? (object)idEl.GetInt32() : idEl.GetRawText(),
                    error = new { code = -32601, message = "unsupported by AgentManager" },
                }));
                yield return new EngineError($"app-server: unsupported server request {method} (auto-rejected)");
            }
            yield break;
        }

        // ---- 알림 ----
        var ps = root.TryGetProperty("params", out var pEl) ? pEl : default;
        switch (method)
        {
            case "item/started" when ps.TryGetProperty("item", out var si):
                foreach (var ev in MapItemStarted(si)) yield return ev;
                break;

            case "item/completed" when ps.TryGetProperty("item", out var ci):
                foreach (var ev in MapItemCompleted(ci)) yield return ev;
                break;

            case "thread/tokenUsage/updated":
                // 누적치 — 매번 emit하면 VM이 중복 가산하므로 마지막 값만 저장해 턴 종료에 싣는다
                if (ps.TryGetProperty("tokenUsage", out var tu) && tu.TryGetProperty("total", out var tot))
                    _lastUsage = new TokenUsage(
                        Lng(tot, "inputTokens"), Lng(tot, "outputTokens"),
                        CacheReadTokens: Lng(tot, "cachedInputTokens"),
                        ReasoningTokens: Lng(tot, "reasoningOutputTokens"));
                break;

            case "account/rateLimits/updated":
                if (ps.TryGetProperty("rateLimits", out var rl) && rl.TryGetProperty("primary", out var pri)
                    && pri.ValueKind == JsonValueKind.Object)
                    yield return new QuotaUpdate(
                        Lng(pri, "usedPercent") / 100.0, // 규약: 0~1 분수 (Claude rate_limit_event와 동일)
                        Lng(pri, "resetsAt"),
                        "codex_primary",
                        "ok");
                break;

            case "error":
                yield return new EngineError(Str(ps, "message") ?? ps.GetRawText());
                _turnFailed = true;
                break;

            case "turn/completed":
                var status = ps.TryGetProperty("turn", out var turn) ? Str(turn, "status") : null;
                yield return new TurnCompleted(null, _turnFailed || status == "failed", null, null, _lastUsage);
                break;

            case "item/agentMessage/delta":
                if (Str(ps, "delta") is { Length: > 0 } d) yield return new AssistantDelta(d);
                break;

            // 내부 진행 알림 — UI 가치 없음
            case "thread/started" or "turn/started" or "thread/status/changed"
                or "item/reasoning/summaryTextDelta" or "item/reasoning/textDelta"
                or "item/reasoning/summaryPartAdded" or "item/commandExecution/outputDelta"
                or "item/fileChange/outputDelta" or "serverRequest/resolved"
                or "mcpServer/startupStatus/updated" or "remoteControl/status/changed"
                or "thread/name/updated" or "turn/diff/updated" or "turn/plan/updated":
                break;

            default:
                yield return new RawUnknown(method ?? "?", line);
                break;
        }
    }

    public string? BuildPermissionResponse(PermissionRequest request, PermissionDecision decision)
        => JsonSerializer.Serialize(new
        {
            id = int.TryParse(request.RequestId, out var n) ? (object)n : request.RequestId,
            result = new { decision = decision.Allow ? (decision.ForSession ? "acceptForSession" : "accept") : "decline" },
        });

    // ---- 핸드셰이크 페이로드 ----

    private string BuildThreadRequest()
    {
        // 샌드박스 대신 승인 게이트: danger-full-access + untrusted (PHASE0_CODEX_APPSERVER_KO §5)
        if (!string.IsNullOrWhiteSpace(_options.ResumeSessionId))
            return JsonSerializer.Serialize(new
            {
                id = ThreadId,
                method = "thread/resume",
                @params = new
                {
                    threadId = _options.ResumeSessionId,
                    cwd = _options.WorkingDirectory,
                    approvalPolicy = "untrusted",
                    sandbox = "danger-full-access",
                },
            });
        return JsonSerializer.Serialize(new
        {
            id = ThreadId,
            method = "thread/start",
            @params = new
            {
                cwd = _options.WorkingDirectory,
                approvalPolicy = "untrusted",
                sandbox = "danger-full-access",
            },
        });
    }

    private string BuildTurnStart()
    {
        var input = new List<object> { new { type = "text", text = _prompt } };
        foreach (var img in _options.Images)
            if (File.Exists(img))
                input.Add(new { type = "localImage", path = img });

        return JsonSerializer.Serialize(new
        {
            id = TurnId,
            method = "turn/start",
            @params = new
            {
                threadId = _threadId,
                input,
                model = string.IsNullOrWhiteSpace(_options.Model) ? null : _options.Model,
                effort = string.IsNullOrWhiteSpace(_options.ReasoningEffort) || _options.ReasoningEffort == "default"
                    ? null : _options.ReasoningEffort,
            },
        });
    }

    // ---- 아이템 매핑 ----

    private static IEnumerable<NormalizedEvent> MapItemStarted(JsonElement item)
    {
        var id = Str(item, "id") ?? "";
        switch (Str(item, "type"))
        {
            case "commandExecution":
                yield return new ToolUseStarted(id, "shell", JsonSerializer.Serialize(new { command = Str(item, "command") }));
                break;
            case "fileChange":
                yield return new ToolUseStarted(id, "apply_patch", item.GetRawText());
                break;
            case "mcpToolCall":
                yield return new ToolUseStarted(id, Str(item, "tool") ?? Str(item, "name") ?? "mcp", item.GetRawText());
                break;
            case "webSearch":
                yield return new ToolUseStarted(id, "web_search", item.GetRawText());
                break;
        }
    }

    private static IEnumerable<NormalizedEvent> MapItemCompleted(JsonElement item)
    {
        var id = Str(item, "id") ?? "";
        switch (Str(item, "type"))
        {
            case "agentMessage":
                var text = Str(item, "text");
                if (!string.IsNullOrWhiteSpace(text)) yield return new AssistantText(text!.Trim());
                break;

            case "reasoning":
                var thought = JoinTexts(item, "summary") ?? JoinTexts(item, "content");
                if (!string.IsNullOrWhiteSpace(thought)) yield return new Thinking(thought!);
                break;

            case "commandExecution":
                var exit = item.TryGetProperty("exitCode", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : 0;
                yield return new ToolResult(
                    id,
                    Str(item, "aggregatedOutput") ?? Str(item, "output") ?? "",
                    exit != 0 || Str(item, "status") == "failed");
                break;

            case "fileChange":
            case "mcpToolCall":
            case "webSearch":
                yield return new ToolResult(id, item.GetRawText(), Str(item, "status") == "failed");
                break;
        }
    }

    private static string ToolNameOf(string method) => method switch
    {
        "item/commandExecution/requestApproval" or "execCommandApproval" => "shell",
        "item/fileChange/requestApproval" or "applyPatchApproval" => "apply_patch",
        "item/permissions/requestApproval" => "permissions",
        _ => method,
    };

    private static string? FindThreadId(JsonElement root)
        => root.TryGetProperty("result", out var r) && r.TryGetProperty("thread", out var t) ? Str(t, "id") : null;

    private static string? JoinTexts(JsonElement item, string prop)
    {
        if (!item.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var parts = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String) { parts.Add(el.GetString()!); continue; }
            if (Str(el, "text") is { } t && !string.IsNullOrWhiteSpace(t)) parts.Add(t);
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Lng(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
