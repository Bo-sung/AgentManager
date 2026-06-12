using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentManager.Core.Events;

namespace AgentManager.Core.Agents;

/// <summary>
/// Antigravity / Gemini CLI 어댑터. `gemini -p ... -o stream-json` (JSONL) 비대화형.
/// 실측: docs/PHASE0_ANTIGRAVITY_GEMINI_KO.md (gemini-cli 0.42.0).
/// Antigravity CLI(6/18 전환)는 같은 표면을 승계할 것으로 보고, exe 해석만 antigravity 우선으로 둔다.
///
/// 헤드리스에 대화형 승인 프로토콜이 없어 Permissions=false — 승인 대신 --approval-mode 매핑:
/// ReadOnly→plan, WorkspaceWrite→auto_edit, DangerFullAccess→yolo(-y).
/// assistant 메시지는 delta 조각으로 오므로 누적 후 경계(tool_use/result)에서 flush한다(상태 보유 — 턴 1회용).
/// </summary>
public sealed class AntigravityAdapter : IAgentAdapter
{
    public string Id => "antigravity";
    public AgentCapabilities Capabilities { get; } = new(
        Permissions: false, Thinking: false, Sessions: true, Images: false, TokenUsage: true, Quota: false);
    public bool CloseStdinAfterStart => true; // stdin은 프롬프트에 덧붙는 입력 — 닫아야 턴이 진행됨

    private readonly StringBuilder _assistantBuffer = new();

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
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--skip-trust"); // 함정 #1: 없으면 신뢰 폴더 게이트로 즉시 실패
        if (options.Sandbox == SandboxMode.ReadOnly)
        { psi.ArgumentList.Add("--approval-mode"); psi.ArgumentList.Add("plan"); }
        else if (options.Sandbox == SandboxMode.WorkspaceWrite && !options.BypassPermissions)
        { psi.ArgumentList.Add("--approval-mode"); psi.ArgumentList.Add("auto_edit"); }
        else
            psi.ArgumentList.Add("-y"); // yolo — 헤드리스 기본 (대화형 승인 프로토콜 없음)
        if (!string.IsNullOrWhiteSpace(options.Model)) { psi.ArgumentList.Add("-m"); psi.ArgumentList.Add(options.Model); }
        if (!string.IsNullOrWhiteSpace(options.ResumeSessionId))
        { psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(options.ResumeSessionId); } // uuid 실측 확인
        foreach (var dir in options.AdditionalDirectories)
            if (Directory.Exists(dir)) { psi.ArgumentList.Add("--include-directories"); psi.ArgumentList.Add(dir); }
        psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(prompt);
        return psi;
    }

    public IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options) => [];

    public IEnumerable<NormalizedEvent> ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;
        JsonElement root;
        try { using var doc = JsonDocument.Parse(line); root = doc.RootElement.Clone(); }
        catch { yield break; }

        switch (Str(root, "type"))
        {
            case "init":
                yield return new SessionStarted(Str(root, "session_id") ?? "", Str(root, "model"), 0, null);
                break;

            case "message" when Str(root, "role") == "assistant":
                // delta 조각: 즉시 스트리밍 표시 + 누적 (경계 이벤트에서 최종 AssistantText로 교체)
                var chunk = Str(root, "content");
                if (!string.IsNullOrEmpty(chunk))
                {
                    _assistantBuffer.Append(chunk);
                    yield return new AssistantDelta(chunk!);
                }
                break;

            case "message": // user echo
                break;

            case "tool_use":
                foreach (var ev in Flush()) yield return ev;
                yield return new ToolUseStarted(
                    Str(root, "tool_id") ?? "",
                    Str(root, "tool_name") ?? "tool",
                    root.TryGetProperty("parameters", out var p) ? p.GetRawText() : "{}");
                break;

            case "tool_result":
                yield return new ToolResult(
                    Str(root, "tool_id") ?? "",
                    Str(root, "output") ?? "",
                    Str(root, "status") is not (null or "success"));
                break;

            case "result":
                foreach (var ev in Flush()) yield return ev;
                var isError = Str(root, "status") == "error";
                if (isError && root.TryGetProperty("error", out var err))
                    yield return new EngineError(Str(err, "message") ?? err.GetRawText());
                if (root.TryGetProperty("stats", out var st))
                    yield return new TokenUsage(
                        Lng(st, "input_tokens"), Lng(st, "output_tokens"),
                        CacheReadTokens: Lng(st, "cached"));
                yield return new TurnCompleted(null, isError, null, null);
                break;

            default:
                yield return new RawUnknown(Str(root, "type") ?? "?", line);
                break;
        }
    }

    private IEnumerable<NormalizedEvent> Flush()
    {
        if (_assistantBuffer.Length == 0) yield break;
        var text = _assistantBuffer.ToString().Trim();
        _assistantBuffer.Clear();
        if (!string.IsNullOrWhiteSpace(text)) yield return new AssistantText(text);
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long Lng(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
}
