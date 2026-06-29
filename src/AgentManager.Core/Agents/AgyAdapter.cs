using System.Diagnostics;
using System.Text.Json;
using AgentManager.Core.Events;
using AgentManager.Core.Hosting;
using static AgentManager.Core.Agents.Win32Args;

namespace AgentManager.Core.Agents;

/// <summary>
/// Antigravity `agy` CLI 어댑터 (v1, 텍스트 전용). 실측: PHASE0_ANTIGRAVITY_GEMINI_KO.md.
///
/// agy 1.0.7은 진짜 콘솔(TTY)에만 출력하므로 ConPTY로 구동한다 (Smoke --agy-pty-check 사용자 세션 PASS).
/// 구조화 이벤트가 없어 도구/토큰/스트리밍은 표시 불가 — 응답 텍스트와 턴 완료만 전달한다.
/// 세션 id는 ~/.gemini/antigravity-cli/cache/last_conversations.json의 cwd→conversation 매핑에서 추출,
/// 다음 턴은 --conversation &lt;id&gt;로 resume.
///
/// 주의: 인증이 사용자 대화형 세션에 묶여 있어 데스크톱에서 실행된 앱에서는 동작하지만
/// 서비스/비대화형 컨텍스트에서는 not-logged-in이 될 수 있다 (실측).
/// </summary>
public sealed class AgyAdapter : IAgentAdapter, IPtyTurnRunner
{
    public string Id => "agy";
    public AgentCapabilities Capabilities { get; } = new(
        Permissions: false, Thinking: false, Sessions: true, Images: false, TokenUsage: false, Quota: false);
    public bool CloseStdinAfterStart => true;

    // stdio 경로 미사용 (IPtyTurnRunner로 구동) — 인터페이스 충족용 최소 구현
    public ProcessStartInfo BuildStartInfo(string executablePath, SessionOptions options, string prompt)
        => throw new NotSupportedException("agy runs via ConPTY (IPtyTurnRunner)");
    public IReadOnlyList<string> InitialStdinLines(string prompt, SessionOptions options) => [];
    public IEnumerable<NormalizedEvent> ParseLine(string line) => [];

    public async Task RunTurnAsync(string executablePath, SessionOptions options, string prompt,
        Func<NormalizedEvent, Task> emitAsync, CancellationToken ct)
    {
        var cmd = BuildCommandLine(executablePath, options, prompt);
        // PTY 엔진에도 env 주입(AGENTMANAGER_TASK_SPOOL 등) — 안 그러면 워커-프롬프트 스킬이 스풀 경로를
        // 못 보고 agy 자체 스크래치(./.am/worker-tasks/)에 써서 백로그로 유입되지 않는다.
        // onOutput 탭으로 raw 스냅샷만 저장(read 스레드) → 폴링 루프가 emit (어댑터 흐름, 순차) → 동시 emit 회피.
        var gate = new object();
        var latestRaw = "";
        var runTask = ConPtyHost.RunAsync(cmd, options.WorkingDirectory, TimeSpan.FromMinutes(10), ct,
            options.ExtraEnvironment, onOutput: snap => { lock (gate) latestRaw = snap; });

        // 라이브 프리뷰: agy가 도는 동안 "깔끔히 append된" 부분만 델타로 흘린다(나레이션이 실시간으로 쌓이게).
        // emit은 이 흐름에서 순차로만 일어나 stdio 엔진과 동일한 한-이벤트씩 순서를 유지하고, 아래 최종
        // AssistantText가 프리뷰를 권위 텍스트로 교체하므로 TUI redraw로 인한 글리치는 자가 치유된다.
        var lastSent = "";
        while (!runTask.IsCompleted)
        {
            try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; }
            string snapRaw; lock (gate) snapRaw = latestRaw;
            var snap = ConPtyHost.StripVt(snapRaw).Trim();
            if (snap.Length > lastSent.Length && snap.StartsWith(lastSent, StringComparison.Ordinal))
                await emitAsync(new AssistantDelta(snap[lastSent.Length..]));
            lastSent = snap; // 항상 최신으로 전진(redraw/분기면 emit 없이 재동기화)
        }

        var (raw, exit) = await runTask;
        var text = ConPtyHost.StripVt(raw).Trim();

        var conversationId = TryReadConversationId(options.WorkingDirectory);
        if (conversationId is not null)
            await emitAsync(new SessionStarted(conversationId, options.Model, 0, options.WorkingDirectory));

        // 최종 권위 텍스트로 스트리밍 프리뷰를 교체(델타가 없었으면 새 블록으로 추가). 에러 경로에서도
        // 먼저 보내, 흘려둔 라이브 블록이 미완 상태로 남지 않게 한다.
        if (!string.IsNullOrWhiteSpace(text))
            await emitAsync(new AssistantText(text));

        if (exit != 0)
        {
            await emitAsync(new EngineError($"agy exited with {exit}: {Truncate(text, 400)}"));
            await emitAsync(new TurnCompleted(null, true, null, null));
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            await emitAsync(new EngineError("agy가 출력 없이 종료됨 — 로그인 상태(터미널에서 agy 실행)를 확인하세요"));
            await emitAsync(new TurnCompleted(null, true, null, null));
            return;
        }

        await emitAsync(new TurnCompleted(null, false, null, null));
    }

    private static string BuildCommandLine(string exe, SessionOptions options, string prompt)
    {
        var parts = new List<string> { Quote(exe), "-p", Quote(prompt), "--dangerously-skip-permissions" };
        if (!string.IsNullOrWhiteSpace(options.Model) && options.Model != "default")
        { parts.Add("--model"); parts.Add(Quote(options.Model)); }
        if (!string.IsNullOrWhiteSpace(options.ResumeSessionId))
        { parts.Add("--conversation"); parts.Add(options.ResumeSessionId!); }
        foreach (var dir in options.AdditionalDirectories)
            if (Directory.Exists(dir)) { parts.Add("--add-dir"); parts.Add(Quote(dir)); }
        return string.Join(" ", parts);
    }

    /// <summary>agy 캐시의 cwd→conversation 매핑에서 이 작업 폴더의 대화 id를 찾는다 (resume용).</summary>
    private static string? TryReadConversationId(string cwd)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gemini", "antigravity-cli", "cache", "last_conversations.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var wanted = Path.GetFullPath(cwd).TrimEnd('\\', '/');
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                if (string.Equals(Path.GetFullPath(prop.Name).TrimEnd('\\', '/'), wanted, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
