using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentManager.Core.Observation;

/// <summary>`claude agents --json` 한 행 — 머신에서 실행 중인 claude 세션/프로세스.</summary>
public sealed record ClaudeAgentRow(
    int Pid,
    string? Cwd,
    string Kind,            // "interactive" | "background" | …
    long StartedAtUnixMs,
    string SessionId);

/// <summary>
/// `claude agents --json` 를 실행/파싱해 머신에서 도는 claude 세션 목록을 얻는다.
/// 실측 스키마(2026-06): [{ "pid", "cwd", "kind", "startedAt"(unix ms), "sessionId" }].
/// 파싱은 순수 함수 — 스모크에서 픽스처로 검증한다.
/// </summary>
public static class ClaudeAgentsProbe
{
    public static IReadOnlyList<ClaudeAgentRow> Parse(string json)
    {
        var rows = new List<ClaudeAgentRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return rows;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var sessionId = Str(el, "sessionId") ?? Str(el, "session_id");
                if (string.IsNullOrWhiteSpace(sessionId)) continue;
                rows.Add(new ClaudeAgentRow(
                    Pid: Int(el, "pid"),
                    Cwd: Str(el, "cwd"),
                    Kind: Str(el, "kind") ?? "unknown",
                    StartedAtUnixMs: Long(el, "startedAt"),
                    SessionId: sessionId!));
            }
        }
        catch { /* 형식 불일치 → 빈 목록 */ }
        return rows;
    }

    /// <summary>CLI를 호출해 파싱한다. 어떤 실패든 빈 목록(관측은 best-effort).</summary>
    public static async Task<IReadOnlyList<ClaudeAgentRow>> RunAsync(
        string exePath, string? workingDirectory = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return [];
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("agents");
            psi.ArgumentList.Add("--json");
            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            using var proc = Process.Start(psi);
            if (proc is null) return [];
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            return Parse(stdout);
        }
        catch
        {
            return [];
        }
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

    private static long Long(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
