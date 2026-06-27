using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    // ----- artifacts (light): derived from events, no extra engine calls -----

    private static string? ExtractCommand(ToolUseStarted u)
    {
        if (u.Name is not ("Bash" or "shell")) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(u.InputJson);
            return doc.RootElement.TryGetProperty("command", out var c) ? c.GetString() : null;
        }
        catch { return null; }
    }

    private static bool IsTestCommand(string cmd) =>
        System.Text.RegularExpressions.Regex.IsMatch(cmd,
            @"\b(dotnet\s+test|npm\s+(run\s+)?test|pytest|vitest|jest|mocha|cargo\s+test|go\s+test)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static ArtifactViewModel GetOrAddArtifact(SessionViewModel s, string kind, string title)
    {
        var a = s.Artifacts.FirstOrDefault(x => x.Kind == kind && x.Title == title);
        if (a is null) { a = new ArtifactViewModel(kind, title); s.Artifacts.Insert(0, a); }
        return a;
    }

    /// <summary>TodoWrite 입력 → 체크리스트 아티팩트(최신 상태로 교체).</summary>
    private static void UpsertTaskListArtifact(SessionViewModel s, string inputJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(inputJson);
            if (!doc.RootElement.TryGetProperty("todos", out var todos) || todos.ValueKind != System.Text.Json.JsonValueKind.Array) return;
            var lines = new List<string>();
            foreach (var t in todos.EnumerateArray())
            {
                var status = t.TryGetProperty("status", out var st) ? st.GetString() : "";
                var mark = status switch { "completed" => "✅", "in_progress" => "🔄", _ => "⬜" };
                var content = t.TryGetProperty("content", out var ct) ? ct.GetString() : "";
                lines.Add($"{mark} {content}");
            }
            if (lines.Count == 0) return;
            GetOrAddArtifact(s, "tasklist", L("L.TaskList")).Content = string.Join("\n", lines);
        }
        catch { /* malformed input — skip */ }
    }

    /// <summary>테스트 러너 실행 결과 → 테스트 아티팩트(명령별 최신 결과).</summary>
    private static void UpsertTestArtifact(SessionViewModel s, string cmd, string output, bool isError)
    {
        var shortCmd = cmd.Length > 60 ? cmd[..60] + "…" : cmd;
        var a = GetOrAddArtifact(s, "test", shortCmd);
        a.IsError = isError;
        var tail = output.Length > 1500 ? "…" + output[^1500..] : output;
        a.Content = (isError ? L("L.TestFailed") : L("L.TestPassed")) + tail;
    }

    /// <summary>턴 종료 시 마지막 어시스턴트 텍스트 → 요약(walkthrough) 아티팩트.</summary>
    private static void UpsertSummaryArtifact(SessionViewModel s)
    {
        var last = s.Transcript.OfType<AgentTextBlock>().LastOrDefault();
        if (last is null || string.IsNullOrWhiteSpace(last.Text)) return;
        GetOrAddArtifact(s, "summary", L("L.Summary")).Content = last.Text;
    }

    /// <summary>gemini가 셸 실행 시 stderr로 쏟는 멀티라인 덤프(xterm.js Parsing error — JS 객체 수십 줄)를
    /// 중괄호 깊이로 추적해 통째로 삼킨다. 라인 단위 패턴으로는 못 잡는 형태.</summary>
    private readonly Dictionary<string, (int Depth, int Ttl)> _stderrDump = [];

    /// <summary>세션별 스트리밍 중인 라이브 응답 블록 (최종 AssistantText 도착 시 교체·해제).</summary>
    private readonly Dictionary<string, AgentTextBlock> _liveText = [];
    private bool SuppressStderr(SessionViewModel s, string m)
    {
        if (IsBenignStderr(m)) return true;
        if (_stderrDump.TryGetValue(s.Id, out var st) && st.Depth > 0)
        {
            var depth = st.Depth + m.Count(c => c == '{') - m.Count(c => c == '}');
            var ttl = st.Ttl - 1;
            _stderrDump[s.Id] = (ttl <= 0 ? 0 : Math.Max(0, depth), ttl); // TTL: 덤프가 잘려도 영원히 삼키지 않게
            return true;
        }
        if (m.Contains("xterm.js: Parsing error"))
        {
            _stderrDump[s.Id] = (Math.Max(1, m.Count(c => c == '{') - m.Count(c => c == '}')), 80);
            return true;
        }
        return false;
    }

    /// <summary>엔진들이 stderr로 흘리는 무해한 안내/경고 — 에러 블록으로 띄우지 않는다.
    /// 진짜 실패는 정규화 이벤트(result/turn.failed/error)로 따로 들어온다.</summary>
    private static bool IsBenignStderr(string m) =>
        m.Contains("Reading additional input")            // codex exec 안내
        || m.Contains("YOLO mode is enabled")             // gemini 승인모드 안내
        || m.Contains("256-color support")                // gemini 터미널 경고
        || m.Contains("Ripgrep is not available")         // gemini 폴백 안내
        || m.Contains("Retrying after")                   // gemini 일시 쿼터 재시도
        || m.Contains("AttachConsole failed")             // gemini node-pty 무해 오류 (실행엔 영향 없음 실측)
        || m.Contains("node-pty") || m.Contains("conpty_console_list")
        || m.Contains("node:internal/") || m.StartsWith("Node.js v")
        || m.TrimStart().StartsWith("at ") || m.Trim() == "^"
        || m.Contains("var consoleProcessList")
        // pi(node)를 턴 종료 후 강제 kill할 때 libuv가 정리 중 내는 무해한 teardown assertion —
        // 워커 출력은 이미 그 전에 생성됨(작업은 성공). 빨간 에러로 띄우지 않는다.
        || m.Contains("UV_HANDLE_CLOSING");

    private static string KindOf(string name) => name switch
    {
        "Read" or "Glob" or "Grep" or "LS" => "READ",
        "Edit" or "MultiEdit" or "Write" => "EDIT",
        _ => "RUN",
    };

    private static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    private static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-')[..Math.Min(28, slug.Trim('-').Length)].TrimEnd('-');
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")))
                return d.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static OllamaTranslator CreateTranslator(string endpoint, string model, string sourceLang = "Korean", string targetLang = "English") =>
        new(new OllamaOptions
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? "exaone3.5:7.8b" : model.Trim(),
            SourceLanguage = string.IsNullOrWhiteSpace(sourceLang) ? "Korean" : sourceLang.Trim(),
            TargetLanguage = string.IsNullOrWhiteSpace(targetLang) ? "English" : targetLang.Trim(),
        });
}
