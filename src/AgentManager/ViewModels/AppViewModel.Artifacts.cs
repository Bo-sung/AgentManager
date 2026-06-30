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
using AgentManager.Core;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    // ----- artifacts (light): derived from events, no extra engine calls -----

    private static bool IsTestCommand(string cmd) => CoreHelpers.IsTestCommand(cmd);

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

    /// <summary>세션별 스트리밍 중인 라이브 응답 블록 (최종 AssistantText 도착 시 교체·해제). 스트리밍
    /// 여부 판단은 Core TranscriptProjector가 소유; 여기선 실제 블록만 보유해 델타를 적용한다.</summary>
    private readonly Dictionary<string, AgentTextBlock> _liveText = [];

    private static string KindOf(string name) => CoreHelpers.KindOf(name);

    private static string Trim(string s, int max) => CoreHelpers.Trim(s, max);

    private static string Slug(string s) => CoreHelpers.Slug(s);

    private static string FindRepoRoot() => CoreHelpers.FindRepoRoot();

    private static OllamaTranslator CreateTranslator(string endpoint, string model, string sourceLang = "Korean", string targetLang = "English") =>
        CoreHelpers.CreateTranslator(endpoint, model, sourceLang, targetLang);
}
