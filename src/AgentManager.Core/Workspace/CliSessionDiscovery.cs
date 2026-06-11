using System.Text.Json;

namespace AgentManager.Core.Workspace;

/// <summary>외부 CLI(클로드/코덱스)가 디스크에 남긴 세션 기록 한 건.</summary>
public sealed record CliHistoryEntry(
    string EngineId,      // "cc" | "gx"
    string SessionId,     // resume에 쓰는 엔진 세션 id
    string Title,         // 첫 사용자 메시지 또는 인덱스의 thread_name
    DateTime LastWriteUtc,
    string FilePath);

/// <summary>AgentManager 밖에서 직접 돌린 claude/codex CLI 세션을 프로젝트 폴더 기준으로 발견한다.
/// - claude: ~/.claude/projects/&lt;경로의 비영숫자를 '-'로 치환한 폴더&gt;/&lt;uuid&gt;.jsonl
/// - codex:  ~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl (첫 줄 session_meta.payload.cwd로 매칭,
///           제목은 ~/.codex/session_index.jsonl의 thread_name 우선)</summary>
public static class CliSessionDiscovery
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static List<CliHistoryEntry> Discover(string projectPath, int maxPerEngine = 30)
    {
        var result = new List<CliHistoryEntry>();
        try { result.AddRange(DiscoverClaude(projectPath, maxPerEngine)); } catch { }
        try { result.AddRange(DiscoverCodex(projectPath, maxPerEngine)); } catch { }
        return result.OrderByDescending(e => e.LastWriteUtc).ToList();
    }

    // ----- claude -----

    /// <summary>claude의 프로젝트 폴더명 규칙: 경로의 비영숫자 문자를 전부 '-'로 치환.</summary>
    public static string ClaudeProjectDirName(string projectPath)
        => new(Path.GetFullPath(projectPath).TrimEnd('\\', '/')
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    public static List<CliHistoryEntry> DiscoverClaude(string projectPath, int max = 30)
    {
        var entries = new List<CliHistoryEntry>();
        var dir = Path.Combine(Home, ".claude", "projects", ClaudeProjectDirName(projectPath));
        if (!Directory.Exists(dir)) return entries;

        foreach (var file in new DirectoryInfo(dir).GetFiles("*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Take(max))
        {
            var sessionId = Path.GetFileNameWithoutExtension(file.Name);
            if (sessionId.Length < 8) continue;
            var title = FirstClaudeUserText(file.FullName);
            if (title is null) continue; // 사용자 메시지가 없는 파일(큐 조각 등)은 스킵
            entries.Add(new CliHistoryEntry("cc", sessionId, Trim(title), file.LastWriteTimeUtc, file.FullName));
        }
        return entries;
    }

    private static string? FirstClaudeUserText(string path)
    {
        foreach (var line in ReadLinesSafe(path, 80))
        {
            if (!line.Contains("\"type\":\"user\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;
                if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) continue;
                if (content.ValueKind == JsonValueKind.String) return content.GetString();
                if (content.ValueKind == JsonValueKind.Array)
                    foreach (var part in content.EnumerateArray())
                        if (part.TryGetProperty("type", out var t) && t.GetString() == "text"
                            && part.TryGetProperty("text", out var txt))
                        {
                            var s = txt.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
            }
            catch { }
        }
        return null;
    }

    // ----- codex -----

    public static List<CliHistoryEntry> DiscoverCodex(string projectPath, int max = 30)
    {
        var entries = new List<CliHistoryEntry>();
        var root = Path.Combine(Home, ".codex", "sessions");
        if (!Directory.Exists(root)) return entries;

        var wanted = Path.GetFullPath(projectPath).TrimEnd('\\', '/');
        var names = LoadCodexIndex();

        // 최근 파일부터 cwd 매칭 (rollout 파일이 많을 수 있어 스캔량 제한)
        var files = new DirectoryInfo(root).GetFiles("rollout-*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc).Take(400);
        foreach (var file in files)
        {
            if (entries.Count >= max) break;
            try
            {
                var first = ReadLinesSafe(file.FullName, 1).FirstOrDefault();
                if (first is null) continue;
                using var doc = JsonDocument.Parse(first);
                if (!doc.RootElement.TryGetProperty("payload", out var meta)) continue;
                if (meta.TryGetProperty("thread_source", out var src) && src.GetString() == "subagent") continue;
                var cwd = meta.TryGetProperty("cwd", out var c) ? c.GetString() : null;
                if (cwd is null || !string.Equals(Path.GetFullPath(cwd).TrimEnd('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                var id = meta.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var title = names.TryGetValue(id!, out var nm) && !string.IsNullOrWhiteSpace(nm)
                    ? nm
                    : FirstCodexUserText(file.FullName) ?? "codex session";
                entries.Add(new CliHistoryEntry("gx", id!, Trim(title), file.LastWriteTimeUtc, file.FullName));
            }
            catch { }
        }
        return entries;
    }

    private static Dictionary<string, string> LoadCodexIndex()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(Home, ".codex", "session_index.jsonl");
        if (!File.Exists(path)) return map;
        foreach (var line in ReadLinesSafe(path, int.MaxValue))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var id = doc.RootElement.TryGetProperty("id", out var i) ? i.GetString() : null;
                var name = doc.RootElement.TryGetProperty("thread_name", out var n) ? n.GetString() : null;
                if (id is not null && name is not null) map[id] = name; // 뒤 항목이 최신
            }
            catch { }
        }
        return map;
    }

    private static string? FirstCodexUserText(string path)
    {
        foreach (var line in ReadLinesSafe(path, 120))
        {
            if (!line.Contains("user_message", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("payload", out var p)
                    && p.TryGetProperty("type", out var t) && t.GetString() == "user_message"
                    && p.TryGetProperty("message", out var m))
                {
                    var s = m.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { }
        }
        return null;
    }

    // ----- transcript reconstruction -----

    /// <summary>가져온 CLI 세션의 과거 대화 한 항목. Role: user | assistant | thinking | tool.</summary>
    public sealed record CliTranscriptItem(string Role, string Name, string Text);

    /// <summary>CLI 기록 파일에서 대화를 복원한다 (표시용 — 도구 출력 등 부피 큰 내용은 요약/생략).</summary>
    public static List<CliTranscriptItem> LoadTranscript(string engineId, string filePath, int maxItems = 400)
        => engineId == "cc" ? LoadClaudeTranscript(filePath, maxItems) : LoadCodexTranscript(filePath, maxItems);

    private static List<CliTranscriptItem> LoadClaudeTranscript(string path, int maxItems)
    {
        var items = new List<CliTranscriptItem>();
        foreach (var line in ReadLinesSafe(path, int.MaxValue))
        {
            if (items.Count >= maxItems) break;
            if (!line.Contains("\"message\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type is not ("user" or "assistant")) continue;
                if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) continue;

                if (content.ValueKind == JsonValueKind.String)
                {
                    var s = content.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) items.Add(new(type!, "", s!));
                    continue;
                }
                if (content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                {
                    var pt = part.TryGetProperty("type", out var ptEl) ? ptEl.GetString() : null;
                    switch (pt)
                    {
                        case "text" when part.TryGetProperty("text", out var txt) && !string.IsNullOrWhiteSpace(txt.GetString()):
                            items.Add(new(type!, "", txt.GetString()!));
                            break;
                        case "thinking" when type == "assistant" && part.TryGetProperty("thinking", out var th) && !string.IsNullOrWhiteSpace(th.GetString()):
                            items.Add(new("thinking", "", th.GetString()!));
                            break;
                        case "tool_use" when type == "assistant":
                            var name = part.TryGetProperty("name", out var nm) ? nm.GetString() ?? "tool" : "tool";
                            var input = part.TryGetProperty("input", out var inp) ? inp.GetRawText() : "";
                            items.Add(new("tool", name, input.Length > 300 ? input[..300] + "…" : input));
                            break;
                    }
                }
            }
            catch { }
        }
        return items;
    }

    private static List<CliTranscriptItem> LoadCodexTranscript(string path, int maxItems)
    {
        var items = new List<CliTranscriptItem>();
        foreach (var line in ReadLinesSafe(path, int.MaxValue))
        {
            if (items.Count >= maxItems) break;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (!root.TryGetProperty("payload", out var p)) continue;
                var pt = p.TryGetProperty("type", out var ptEl) ? ptEl.GetString() : null;

                // response_item의 user role은 환경 컨텍스트 덤프가 섞여 있어 event_msg만 신뢰한다
                if (type == "event_msg")
                {
                    switch (pt)
                    {
                        case "user_message" when p.TryGetProperty("message", out var um) && !string.IsNullOrWhiteSpace(um.GetString()):
                            items.Add(new("user", "", um.GetString()!));
                            break;
                        case "agent_message" when p.TryGetProperty("message", out var am) && !string.IsNullOrWhiteSpace(am.GetString()):
                            items.Add(new("assistant", "", am.GetString()!));
                            break;
                        case "agent_reasoning" when p.TryGetProperty("text", out var rt) && !string.IsNullOrWhiteSpace(rt.GetString()):
                            items.Add(new("thinking", "", rt.GetString()!));
                            break;
                    }
                }
                else if (type == "response_item" && pt is "function_call" or "local_shell_call" or "custom_tool_call")
                {
                    var name = p.TryGetProperty("name", out var nm) ? nm.GetString() ?? "tool" : "tool";
                    var args = p.TryGetProperty("arguments", out var ar) ? ar.GetString() ?? "" : "";
                    items.Add(new("tool", name, args.Length > 300 ? args[..300] + "…" : args));
                }
            }
            catch { }
        }
        return items;
    }

    // ----- shared -----

    private static IEnumerable<string> ReadLinesSafe(string path, int maxLines)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        for (var i = 0; i < maxLines && reader.ReadLine() is { } line; i++)
            yield return line;
    }

    private static string Trim(string s)
    {
        var t = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return t.Length > 80 ? t[..80] + "…" : t;
    }
}
