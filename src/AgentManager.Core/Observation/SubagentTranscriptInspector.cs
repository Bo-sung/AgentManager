using System.Text;
using System.Text.Json;

namespace AgentManager.Core.Observation;

/// <summary>subagent transcript 한 건에서 추론한 실패 상태.</summary>
public sealed record SubagentFailure(bool Failed, bool RateLimited, string? Message);

/// <summary>
/// subagent transcript(JSONL)을 훑어 API 오류/주간·사용량 한도 종료를 추론한다.
/// 훅(SubagentStop)이 상태를 못 싣거나 안 떴을 때의 폴백.
///
/// 실측(2026-06): rate-limit 종료 라인 =
///   { "type":"assistant", "isApiErrorMessage":true,
///     "message":{ "content":[{ "type":"text", "text":"You've hit your weekly limit … resets …" }] } }
/// 정상 종료는 마지막 assistant 의 message.stop_reason = "end_turn".
/// </summary>
public static class SubagentTranscriptInspector
{
    private static readonly string[] RateLimitHints =
        ["weekly limit", "usage limit", "rate limit", "rate_limit", "5-hour limit", "five hour", "limit · resets", "limit, resets"];

    /// <summary>없거나 정상이면 Failed=false. API 오류 라인이 있으면 Failed=true(+rate-limit 판정·메시지).</summary>
    public static SubagentFailure Inspect(string transcriptPath)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath) || !File.Exists(transcriptPath))
            return new SubagentFailure(false, false, null);
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            SubagentFailure? failure = null;
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                var found = InspectLine(line);
                if (found is not null) failure = found; // 마지막 오류 라인을 채택
            }
            return failure ?? new SubagentFailure(false, false, null);
        }
        catch
        {
            return new SubagentFailure(false, false, null);
        }
    }

    /// <summary>임의의 텍스트(예: 훅의 last_assistant_message)가 한도 종료 문구처럼 보이는가.</summary>
    public static bool LooksLikeLimit(string? text)
        => !string.IsNullOrWhiteSpace(text)
           && RateLimitHints.Any(h => text!.Contains(h, StringComparison.OrdinalIgnoreCase));

    /// <summary>JSONL 한 줄 검사. API 오류면 SubagentFailure, 아니면 null. (스모크용 공개)</summary>
    public static SubagentFailure? InspectLine(string line)
    {
        // 빠른 선별 — 대부분 라인은 오류 플래그가 없다.
        if (!line.Contains("isApiErrorMessage", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!(root.TryGetProperty("isApiErrorMessage", out var flag)
                  && flag.ValueKind == JsonValueKind.True))
                return null;

            var text = ExtractText(root);
            var rateLimited = text is not null
                && RateLimitHints.Any(h => text.Contains(h, StringComparison.OrdinalIgnoreCase));
            return new SubagentFailure(true, rateLimited, text);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return null;
        if (!msg.TryGetProperty("content", out var content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var t)
                    && t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        return null;
    }
}
