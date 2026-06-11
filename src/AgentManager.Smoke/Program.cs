// Headless smoke tests — adapter parsing/args (zero tokens) + GitWorktree
// end-to-end against a throwaway temp repo (zero tokens, real git).
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

// Live approval round-trip (costs a few engine tokens): dotnet run -- --live-approval
if (args.Contains("--live-approval"))
{
    await LiveApprovalAsync();
    return;
}

// Full product E2E (real Claude + Ollama translation + worktree + merge): dotnet run -- --e2e
if (args.Contains("--e2e"))
{
    await E2EAsync();
    return;
}

// Stage 2 spike: codex app-server JSON-RPC round-trip incl. interactive approval.
// dotnet run -- --appserver-probe
if (args.Contains("--appserver-probe"))
{
    await AppServerProbeAsync();
    return;
}

// Stage 2 integration test: the real product path (AgentSession + CodexAppServerAdapter)
// with an auto-accepting PermissionHandler. dotnet run -- --live-stage2
if (args.Contains("--live-stage2"))
{
    await LiveStage2Async();
    return;
}

static async Task LiveStage2Async()
{
    static string? FindCodexExe()
    {
        var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(ext)) return null;
        return Directory.EnumerateDirectories(ext, "openai.chatgpt-*")
            .Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    var exe = FindCodexExe();
    if (exe is null) { Console.WriteLine("[stage2] codex.exe not found"); return; }
    var tmp = Path.Combine(Path.GetTempPath(), "am_st2_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    Console.WriteLine($"[stage2] cwd={tmp}");

    var approvals = 0;
    string? sessionId = null;
    var sawToolStartAndResult = (start: false, result: false);
    var turnDone = false;

    var session = new AgentSession(new CodexAppServerAdapter(), exe);
    session.PermissionHandler = pr =>
    {
        approvals++;
        Console.WriteLine($"[stage2] APPROVAL {pr.ToolName} req={pr.RequestId} -> accept");
        return Task.FromResult(new PermissionDecision(true));
    };
    session.EventReceived += ev =>
    {
        Console.WriteLine("  " + Describe(ev));
        switch (ev)
        {
            case SessionStarted s: sessionId = s.SessionId; break;
            case ToolUseStarted: sawToolStartAndResult.start = true; break;
            case ToolResult: sawToolStartAndResult.result = true; break;
            case TurnCompleted: turnDone = true; break;
        }
    };

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    await session.RunAsync(
        new SessionOptions { WorkingDirectory = tmp },
        "Run a shell command that writes the exact text stage2-integration into a new file named probe.txt, then stop.",
        cts.Token);

    var ok = File.Exists(Path.Combine(tmp, "probe.txt"))
             && (await File.ReadAllTextAsync(Path.Combine(tmp, "probe.txt"))).Contains("stage2-integration");
    Console.WriteLine($"[stage2] approvals={approvals} session={sessionId} tool={sawToolStartAndResult} turnDone={turnDone} fileOk={ok}");
    Console.WriteLine(ok && approvals > 0 && turnDone && sessionId is not null ? "stage2 integration PASS" : "stage2 integration FAIL");
    try { Directory.Delete(tmp, recursive: true); } catch { }
}

static async Task AppServerProbeAsync()
{
    static string? FindCodex()
    {
        var ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(ext)) return null;
        return Directory.EnumerateDirectories(ext, "openai.chatgpt-*")
            .Select(d => Path.Combine(d, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    var codex = FindCodex();
    if (codex is null) { Console.WriteLine("[appserver] codex.exe not found"); return; }

    var tmp = Path.Combine(Path.GetTempPath(), "am_aps_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    Console.WriteLine($"[appserver] exe={codex}");
    Console.WriteLine($"[appserver] cwd={tmp}");

    var utf8 = new System.Text.UTF8Encoding(false);
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = codex,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = utf8,
        StandardErrorEncoding = utf8,
        StandardInputEncoding = utf8,
    };
    psi.ArgumentList.Add("app-server");
    using var p = System.Diagnostics.Process.Start(psi)!;
    _ = Task.Run(async () => { while (await p.StandardError.ReadLineAsync() is { } e) Console.WriteLine("[stderr] " + e); });

    async Task Send(object msg)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(msg);
        Console.WriteLine("--> " + (json.Length > 220 ? json[..220] + "…" : json));
        await p.StandardInput.WriteLineAsync(json);
        await p.StandardInput.FlushAsync();
    }

    await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "AgentManager", title = "AgentManager", version = "0.1.0" } } });

    string? threadId = null;
    var approvals = 0;
    var ok = false;
    var deadline = DateTime.UtcNow.AddSeconds(180);
    while (DateTime.UtcNow < deadline)
    {
        var readTask = p.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30))) != readTask) { Console.WriteLine("[appserver] read timeout"); break; }
        var line = readTask.Result;
        if (line is null) { Console.WriteLine("[appserver] EOF"); break; }
        Console.WriteLine("<-- " + (line.Length > 220 ? line[..220] + "…" : line));

        System.Text.Json.JsonDocument doc;
        try { doc = System.Text.Json.JsonDocument.Parse(line); } catch { continue; }
        using var _d = doc;
        var root = doc.RootElement;
        var method = root.TryGetProperty("method", out var mEl) ? mEl.GetString() : null;
        var hasId = root.TryGetProperty("id", out var idEl);

        if (method is null && hasId) // response to one of our requests
        {
            var id = idEl.GetInt32();
            if (id == 1)
            {
                await Send(new { method = "initialized" });
                // sandbox는 danger-full-access: 이 환경에서 codex 윈도우 샌드박스 spawn이 실패하고
                // (제품 모델도 "샌드박스 대신 승인 게이트"), 승인은 approvalPolicy가 강제한다
                await Send(new
                {
                    id = 2,
                    method = "thread/start",
                    @params = new { cwd = tmp, approvalPolicy = "untrusted", sandbox = "danger-full-access" }
                });
            }
            else if (id == 2)
            {
                // ThreadStartResponse: find the thread id wherever it lives
                threadId = FindString(root, "threadId") ?? FindString(root, "id");
                Console.WriteLine($"[appserver] threadId={threadId}");
                await Send(new
                {
                    id = 3,
                    method = "turn/start",
                    @params = new
                    {
                        threadId,
                        input = new object[] { new { type = "text", text = "Run a shell command that writes the exact text stage2-approval-spike into a new file named probe.txt, then stop." } },
                    }
                });
            }
            continue;
        }

        if (method is not null && hasId) // server -> client request (approval etc.)
        {
            Console.WriteLine($"[appserver] SERVER REQUEST {method}");
            if (method.Contains("requestApproval") || method is "execCommandApproval" or "applyPatchApproval")
            {
                approvals++;
                await Send(new { id = idEl.GetInt32(), result = new { decision = "accept" } });
            }
            continue;
        }

        if (method == "turn/completed") { ok = true; break; }
        if (method == "error") Console.WriteLine("[appserver] ERROR notification");
    }

    var probe = Path.Combine(tmp, "probe.txt");
    var fileOk = File.Exists(probe) && (await File.ReadAllTextAsync(probe)).Contains("stage2-approval-spike");
    Console.WriteLine($"[appserver] approvals={approvals} turnCompleted={ok} fileOk={fileOk}");
    Console.WriteLine(ok && fileOk && approvals > 0 ? "appserver probe PASS" : "appserver probe FAIL");
    try { p.Kill(entireProcessTree: true); } catch { }
    try { Directory.Delete(tmp, recursive: true); } catch { }

    static string? FindString(System.Text.Json.JsonElement el, string name)
    {
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name == name && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String) return prop.Value.GetString();
                if (FindString(prop.Value, name) is { } s) return s;
            }
        }
        else if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                if (FindString(item, name) is { } s) return s;
        }
        return null;
    }
}

// External CLI session discovery against the real disk: dotnet run -- --cli-history <projectPath>
if (args.Length >= 2 && args[0] == "--cli-history")
{
    var found = CliSessionDiscovery.Discover(args[1]);
    Console.WriteLine($"[cli-history] {args[1]} -> {found.Count} entries");
    foreach (var e in found)
        Console.WriteLine($"  {e.EngineId} {e.SessionId[..Math.Min(12, e.SessionId.Length)]}… {e.LastWriteUtc:MM-dd HH:mm} | {e.Title}");
    foreach (var e in found.GroupBy(x => x.EngineId).Select(g => g.First()))
    {
        var tr = CliSessionDiscovery.LoadTranscript(e.EngineId, e.FilePath);
        Console.WriteLine($"[transcript] {e.EngineId} {e.SessionId[..8]}… -> {tr.Count} items");
        foreach (var it in tr.Take(6))
            Console.WriteLine($"  {it.Role,-9} {it.Name,-12} {(it.Text.Length > 70 ? it.Text[..70].Replace('\n', ' ') + "…" : it.Text.Replace('\n', ' '))}");
    }
    return;
}

static async Task E2EAsync()
{
    static string Pass(bool ok) => ok ? "PASS" : "FAIL";
    var tmp = Path.Combine(Path.GetTempPath(), "am_e2e_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    var wtRoot = Path.Combine(tmp, "_wts");
    try
    {
        static async Task Git(string dir, params string[] a)
        {
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "git", WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            foreach (var x in a) psi.ArgumentList.Add(x);
            using var p = System.Diagnostics.Process.Start(psi)!; await p.WaitForExitAsync();
        }
        // 1. project git repo
        await Git(tmp, "init", "-q");
        await Git(tmp, "config", "user.email", "t@t"); await Git(tmp, "config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(tmp, "README.md"), "# e2e\n");
        await Git(tmp, "add", "-A"); await Git(tmp, "commit", "-qm", "init");
        Console.WriteLine("[1] project repo ready");

        // 2. worktree isolation
        var wt = await GitWorktree.CreateAsync(tmp, "s1", "agent/e2e", wtRoot);
        Console.WriteLine($"[2] worktree isolation .......... {Pass(wt is not null && Directory.Exists(wt!.Path))}");
        if (wt is null) { Console.WriteLine("E2E ABORTED (not a git repo?)"); return; }

        // 3. translation + engine: Korean prompt -> KO->EN -> Claude writes a file -> EN->KO response
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        var translator = new OllamaTranslator(new OllamaOptions());
        var session = new AgentSession(new ClaudeAdapter(), File.Exists(exe) ? exe : "claude", translator, translationEnabled: true);

        string? koreanReply = null; bool sawTool = false;
        session.EventReceived += ev =>
        {
            switch (ev)
            {
                case ToolUseStarted u: sawTool = true; Console.WriteLine($"    tool: {u.Name}"); break;
                case AssistantText t: koreanReply = t.Text; break;
            }
        };
        var opts = new SessionOptions { WorkingDirectory = wt.Path, BypassPermissions = true };
        Console.WriteLine("[3] running Claude with Korean prompt (translation ON)…");
        await session.RunAsync(opts, "fibonacci.txt 파일을 만들어서 피보나치 수열의 처음 8개 숫자를 적어줘. Write 도구를 쓰고 끝나면 멈춰.");

        var file = Path.Combine(wt.Path, "fibonacci.txt");
        var fileMade = File.Exists(file);
        var replyKorean = koreanReply is not null && System.Text.RegularExpressions.Regex.IsMatch(koreanReply, "[가-힣]");
        Console.WriteLine($"[3a] engine used a tool ......... {Pass(sawTool)}");
        Console.WriteLine($"[3b] file created in worktree ... {Pass(fileMade)}  ({(fileMade ? "fibonacci.txt" : "missing")})");
        Console.WriteLine($"[3c] reply translated to KO ..... {Pass(replyKorean)}  (\"{(koreanReply ?? "").Replace("\n", " ")[..Math.Min(60, (koreanReply ?? "").Length)]}\")");

        // 4. review: changed files + diff
        var changes = await GitWorktree.GetChangedFilesAsync(wt.Path);
        var diff = await GitWorktree.GetDiffAsync(wt.Path);
        Console.WriteLine($"[4] review: {changes.Count} changed file(s), diff {diff.Length} chars .. {Pass(changes.Count >= 1 && diff.Length > 0)}");

        // 5. merge -> main updated, then cleanup worktree
        var (mok, mmsg) = await GitWorktree.MergeAsync(tmp, "agent/e2e", "agent: e2e", wt.Path);
        var onMain = File.Exists(Path.Combine(tmp, "fibonacci.txt"));
        Console.WriteLine($"[5] merge to main .............. {Pass(mok && onMain)}  ({mmsg})");
        await GitWorktree.RemoveAsync(tmp, wt.Path);

        var allOk = wt is not null && fileMade && sawTool && replyKorean && changes.Count >= 1 && mok && onMain;
        Console.WriteLine(allOk ? "\nE2E OK — full product path verified" : "\nE2E had failures (see above)");
    }
    finally { try { Directory.Delete(tmp, true); } catch { } }
}

static async Task LiveApprovalAsync()
{
    var tmp = Path.Combine(Path.GetTempPath(), "am_appr_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    try
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        var session = new AgentSession(new ClaudeAdapter(), File.Exists(exe) ? exe : "claude");
        int asked = 0;
        session.PermissionHandler = pr =>
        {
            asked++;
            Console.WriteLine($"  PermissionRequest #{asked}: {pr.ToolName} → AUTO-ALLOW");
            return Task.FromResult(new PermissionDecision(true));
        };
        session.EventReceived += ev => { if (ev is AssistantText t) Console.WriteLine("  agent: " + t.Text); };

        var options = new SessionOptions { WorkingDirectory = tmp, BypassPermissions = false };
        await session.RunAsync(options, "Create a file named ok.txt containing exactly OK. Use the Write tool. Then stop.");

        var created = File.Exists(Path.Combine(tmp, "ok.txt"));
        Console.WriteLine($"approval round-trip: asked={asked}, file created={created}");
        Console.WriteLine(asked > 0 && created ? "LIVE APPROVAL OK" : "LIVE APPROVAL FAILED");
    }
    finally
    {
        try { Directory.Delete(tmp, true); } catch { }
    }
}

string[] claudeLines =
[
    """{"type":"system","subtype":"init","session_id":"sess-1","model":"claude-sonnet-4-6","tools":[1,2,3],"cwd":"J:\\prj\\AgentManager"}""",
    """{"type":"rate_limit_event","session_id":"sess-1","rate_limit_info":{"status":"allowed_warning","resetsAt":1781132400,"rateLimitType":"seven_day","utilization":0.76}}""",
    """{"type":"assistant","parent_tool_use_id":null,"message":{"usage":{"input_tokens":3,"output_tokens":8,"cache_read_input_tokens":15718,"cache_creation_input_tokens":5745},"content":[{"type":"thinking","thinking":"let me run it"}]}}""",
    """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"echo hi"}}]}}""",
    """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"hi","is_error":false}]}}""",
    """{"type":"assistant","parent_tool_use_id":"toolu_sub","message":{"content":[{"type":"text","text":"all done"}]}}""",
    """{"type":"result","subtype":"success","is_error":false,"result":"all done","total_cost_usd":0.0123,"num_turns":1}""",
];

string[] codexLines =
[
    """{"type":"thread.started","thread_id":"019ea6ad"}""",
    """{"type":"turn.started"}""",
    """{"type":"item.started","item":{"id":"item_0","type":"command_execution","command":"echo codex-spike","status":"in_progress"}}""",
    """{"type":"item.completed","item":{"id":"item_0","type":"command_execution","aggregated_output":"codex-spike\r\n","exit_code":0,"status":"completed"}}""",
    """{"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"codex-spike"}}""",
    """{"type":"turn.completed","usage":{"input_tokens":27644,"cached_input_tokens":18688,"output_tokens":47,"reasoning_output_tokens":0}}""",
];

void Run(string title, IAgentAdapter adapter, string[] lines)
{
    Console.WriteLine($"=== {title} ({adapter.Id}) ===");
    foreach (var line in lines)
        foreach (var ev in adapter.ParseLine(line))
            Console.WriteLine("  " + Describe(ev));
    Console.WriteLine();
}

static string Describe(NormalizedEvent ev) => ev switch
{
    SessionStarted s => $"SessionStarted id={s.SessionId} model={s.Model} tools={s.ToolCount}",
    QuotaUpdate q => $"QuotaUpdate {q.Utilization:P0} type={q.RateLimitType} status={q.Status}",
    Thinking t => $"Thinking \"{Trunc(t.Text)}\"",
    ToolUseStarted u => $"ToolUseStarted {u.Name} id={u.ToolUseId} input={Trunc(u.InputJson)}",
    ToolResult r => $"ToolResult id={r.ToolUseId} err={r.IsError} sub={r.FromSubagent} \"{Trunc(r.Content)}\"",
    AssistantText a => $"AssistantText sub={a.FromSubagent} \"{Trunc(a.Text)}\"",
    TokenUsage k => $"TokenUsage in={k.InputTokens} out={k.OutputTokens} cacheRead={k.CacheReadTokens} reasoning={k.ReasoningTokens}",
    PermissionRequest p => $"PermissionRequest {p.ToolName} req={p.RequestId}",
    TurnCompleted c => $"TurnCompleted err={c.IsError} cost={c.CostUsd} turns={c.NumTurns}",
    EngineError e => $"EngineError {e.Message}",
    RawUnknown x => $"RawUnknown type={x.Type}",
    _ => ev.ToString() ?? "?",
};

static string Trunc(string s) => s.Length > 40 ? s[..40] + "…" : s;

Run("Claude stream-json", new ClaudeAdapter(), claudeLines);
Run("Codex exec --json", new CodexAdapter(), codexLines);
AssertResumeArgs();
AssertSandboxAndModelArgs();
AssertPermissionResponse();
AssertAppServerAdapter();

static void AssertAppServerAdapter()
{
    var cwd = Environment.CurrentDirectory;

    static List<NormalizedEvent> Parse(IAgentAdapter a, string line) => a.ParseLine(line).ToList();

    // --- handshake: initialize 응답 → initialized + thread/start writeback ---
    var ad = (IAgentAdapter)new CodexAppServerAdapter();
    var init = ad.InitialStdinLines("hello", new SessionOptions { WorkingDirectory = cwd });
    Assert(init.Count == 1 && init[0].Contains("\"initialize\"") && init[0].Contains("AgentManager"), "appserver initialize line");

    var wb1 = Parse(ad, """{"id":1,"result":{"userAgent":"x"}}""");
    Assert(wb1.Count == 2 && wb1.All(e => e is EngineWriteback), "appserver init response -> 2 writebacks");
    Assert(((EngineWriteback)wb1[0]).Line.Contains("\"initialized\""), "appserver initialized notification");
    var ts = ((EngineWriteback)wb1[1]).Line;
    Assert(ts.Contains("thread/start") && ts.Contains("untrusted") && ts.Contains("danger-full-access"), "appserver thread/start policy");

    // --- thread/start 응답 → SessionStarted + turn/start writeback ---
    var wb2 = Parse(ad, """{"id":2,"result":{"thread":{"id":"th-123","sessionId":"th-123"}}}""");
    Assert(wb2.Count == 2 && wb2[0] is SessionStarted { SessionId: "th-123" }, "appserver SessionStarted");
    var turn = ((EngineWriteback)wb2[1]).Line;
    Assert(turn.Contains("turn/start") && turn.Contains("th-123") && turn.Contains("hello"), "appserver turn/start payload");

    // --- 승인 요청 → PermissionRequest → accept/decline 응답 포맷 ---
    var req = Parse(ad, """{"method":"item/commandExecution/requestApproval","id":0,"params":{"threadId":"th-123","itemId":"call_1"}}""");
    Assert(req.Count == 1 && req[0] is PermissionRequest { RequestId: "0", ToolName: "shell", ToolUseId: "call_1" }, "appserver PermissionRequest");
    var allow = ad.BuildPermissionResponse((PermissionRequest)req[0], new PermissionDecision(true));
    Assert(allow == """{"id":0,"result":{"decision":"accept"}}""", "appserver accept json");
    var deny = ad.BuildPermissionResponse((PermissionRequest)req[0], new PermissionDecision(false, "no"));
    Assert(deny == """{"id":0,"result":{"decision":"decline"}}""", "appserver decline json");

    // --- 아이템/턴 매핑 ---
    var tool = Parse(ad, """{"method":"item/started","params":{"item":{"type":"commandExecution","id":"call_1","command":"echo hi"}}}""");
    Assert(tool.Count == 1 && tool[0] is ToolUseStarted { Name: "shell", ToolUseId: "call_1" }, "appserver ToolUseStarted");
    var toolDone = Parse(ad, """{"method":"item/completed","params":{"item":{"type":"commandExecution","id":"call_1","aggregatedOutput":"hi","exitCode":0,"status":"completed"}}}""");
    Assert(toolDone.Count == 1 && toolDone[0] is ToolResult { Content: "hi", IsError: false }, "appserver ToolResult");
    var msg = Parse(ad, """{"method":"item/completed","params":{"item":{"type":"agentMessage","id":"m1","text":"done!"}}}""");
    Assert(msg.Count == 1 && msg[0] is AssistantText { Text: "done!" }, "appserver AssistantText");
    Parse(ad, """{"method":"thread/tokenUsage/updated","params":{"tokenUsage":{"total":{"inputTokens":100,"outputTokens":7,"cachedInputTokens":50}}}}""");
    var done = Parse(ad, """{"method":"turn/completed","params":{"turn":{"id":"t1","status":"completed"}}}""");
    Assert(done.Count == 1 && done[0] is TurnCompleted { IsError: false, Usage: { InputTokens: 100, OutputTokens: 7, CacheReadTokens: 50 } }, "appserver TurnCompleted+usage");

    // --- 미지원 서버 요청은 에러 응답으로 차단 해제 ---
    var unsup = Parse(ad, """{"method":"item/tool/requestUserInput","id":9,"params":{}}""");
    Assert(unsup.Count == 2 && unsup[0] is EngineWriteback w && w.Line.Contains("-32601") && unsup[1] is EngineError, "appserver unsupported server request");

    // --- resume 경로 ---
    var ad2 = (IAgentAdapter)new CodexAppServerAdapter();
    ad2.InitialStdinLines("again", new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "th-old" });
    var rs = Parse(ad2, """{"id":1,"result":{}}""");
    var resume = ((EngineWriteback)rs[1]).Line;
    Assert(resume.Contains("thread/resume") && resume.Contains("th-old"), "appserver thread/resume");

    Console.WriteLine("codex app-server adapter asserts OK");
}
await TestGitWorktreeAsync();
Console.WriteLine("smoke OK");

static void AssertPermissionResponse()
{
    var req = new PermissionRequest("req-1", "Write", """{"file_path":"a.txt","content":"hi"}""", "toolu_9");
    var allow = new ClaudeAdapter().BuildPermissionResponse(req, new PermissionDecision(true));
    Assert(allow is not null && allow.Contains("\"behavior\":\"allow\"") && allow.Contains("req-1")
        && allow.Contains("toolu_9") && allow.Contains("a.txt"), "Claude allow response");
    var deny = new ClaudeAdapter().BuildPermissionResponse(req, new PermissionDecision(false, "nope"));
    Assert(deny is not null && deny.Contains("\"behavior\":\"deny\"") && deny.Contains("nope")
        && deny.Contains("\"interrupt\":true"), "Claude deny response");
    Assert(((IAgentAdapter)new CodexAdapter()).BuildPermissionResponse(req, new PermissionDecision(true)) is null,
        "Codex has no approval protocol (null)");
    Console.WriteLine("permission response asserts OK");
}

static void AssertSandboxAndModelArgs()
{
    var cwd = Environment.CurrentDirectory;

    // Codex sandbox mapping
    string[] CodexArgs(SandboxMode sb, bool bypass) =>
        new CodexAdapter().BuildStartInfo("codex",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = bypass, Sandbox = sb }, "p").ArgumentList.ToArray();
    Assert(CodexArgs(SandboxMode.DangerFullAccess, true).Contains("--dangerously-bypass-approvals-and-sandbox"), "Codex danger+bypass");
    var ro = CodexArgs(SandboxMode.ReadOnly, true);
    Assert(ro.Contains("--sandbox") && ro.Contains("read-only"), "Codex read-only");
    var ww = CodexArgs(SandboxMode.WorkspaceWrite, true);
    Assert(ww.Contains("--sandbox") && ww.Contains("workspace-write"), "Codex workspace-write");

    // Claude sandbox + model mapping
    string[] ClaudeArgs(SandboxMode sb, string? model = null) =>
        new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, Sandbox = sb, Model = model }, "p").ArgumentList.ToArray();
    var plan = ClaudeArgs(SandboxMode.ReadOnly);
    Assert(plan.Contains("--permission-mode") && plan.Contains("plan"), "Claude read-only→plan");
    Assert(ClaudeArgs(SandboxMode.DangerFullAccess).Contains("--dangerously-skip-permissions"), "Claude bypass");
    var cm = ClaudeArgs(SandboxMode.DangerFullAccess, "sonnet");
    Assert(cm.Contains("--model") && cm.Contains("sonnet"), "Claude --model");
    var xm = new CodexAdapter().BuildStartInfo("codex",
        new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, Model = "gpt-5.1" }, "p").ArgumentList.ToArray();
    Assert(xm.Contains("-m") && xm.Contains("gpt-5.1"), "Codex -m");

    // MCP passthrough: existing file → --mcp-config; missing file → omitted
    var mcpFile = Path.GetTempFileName();
    try
    {
        var with = new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, McpConfigPath = mcpFile }, "p").ArgumentList.ToArray();
        Assert(with.Contains("--mcp-config") && with.Contains(mcpFile), "Claude --mcp-config");
        var without = new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, McpConfigPath = mcpFile + ".missing" }, "p").ArgumentList.ToArray();
        Assert(!without.Contains("--mcp-config"), "missing mcp file omitted");
    }
    finally { File.Delete(mcpFile); }

    // multi-folder: existing extra dir → claude --add-dir / codex writable_roots; missing dir omitted
    var extra = Directory.CreateTempSubdirectory("am_extra_").FullName;
    try
    {
        var cAdd = new ClaudeAdapter().BuildStartInfo("claude",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, AdditionalDirectories = [extra, extra + "_missing"] }, "p").ArgumentList.ToArray();
        Assert(cAdd.Contains("--add-dir") && cAdd.Contains(extra) && !cAdd.Contains(extra + "_missing"), "Claude --add-dir");
        var xAdd = new CodexAdapter().BuildStartInfo("codex",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = false, Sandbox = SandboxMode.WorkspaceWrite, AdditionalDirectories = [extra] }, "p").ArgumentList.ToArray();
        Assert(xAdd.Contains("-c") && xAdd.Any(a => a.StartsWith("sandbox_workspace_write.writable_roots=[") && a.Contains(extra.Replace('\\', '/'))), "Codex writable_roots");
        var xDanger = new CodexAdapter().BuildStartInfo("codex",
            new SessionOptions { WorkingDirectory = cwd, BypassPermissions = true, Sandbox = SandboxMode.DangerFullAccess, AdditionalDirectories = [extra] }, "p").ArgumentList.ToArray();
        Assert(!xDanger.Any(a => a.Contains("writable_roots")), "Codex danger: no writable_roots needed");
    }
    finally { Directory.Delete(extra); }
    Console.WriteLine("sandbox/model/mcp/add-dir arg asserts OK");
}

static async Task TestGitWorktreeAsync()
{
    var tmp = Path.Combine(Path.GetTempPath(), "am_smoke_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    try
    {
        async Task<string> Git(params string[] args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo { FileName = "git", WorkingDirectory = tmp, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return o;
        }
        await Git("init", "-q");
        await Git("config", "user.email", "t@t"); await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(tmp, "a.txt"), "base\n");
        await Git("add", "-A"); await Git("commit", "-qm", "init");

        // create
        var wtRoot = Path.Combine(tmp, "_wts");
        var wt = await GitWorktree.CreateAsync(tmp, "s1", "agent/s1", wtRoot);
        Assert(wt is not null && Directory.Exists(wt.Path), "worktree create");

        // change detection (modified + untracked) and diff incl. untracked
        await File.WriteAllTextAsync(Path.Combine(wt!.Path, "a.txt"), "changed\n");
        await File.WriteAllTextAsync(Path.Combine(wt.Path, "new.txt"), "hello\n");
        var changes = await GitWorktree.GetChangedFilesAsync(wt.Path);
        Assert(changes.Count == 2, $"changes count = {changes.Count} (expected 2)");
        var diff = await GitWorktree.GetDiffAsync(wt.Path);
        Assert(diff.Contains("changed") && diff.Contains("new.txt"), "diff incl. untracked");

        // discard → clean
        var (dok, _) = await GitWorktree.DiscardAsync(wt.Path);
        Assert(dok && (await GitWorktree.GetChangedFilesAsync(wt.Path)).Count == 0, "discard cleans");

        // commit-only → branch ahead, main untouched
        await File.WriteAllTextAsync(Path.Combine(wt.Path, "a.txt"), "feature\n");
        var (cok, _) = await GitWorktree.CommitAsync(wt.Path, "agent: c1");
        Assert(cok, "commit-only");
        Assert((await File.ReadAllTextAsync(Path.Combine(tmp, "a.txt"))).StartsWith("base"), "main untouched after commit-only");

        // merge (second change) → main updated
        await File.WriteAllTextAsync(Path.Combine(wt.Path, "a.txt"), "feature2\n");
        var (mok, mmsg) = await GitWorktree.MergeAsync(tmp, "agent/s1", "agent: c2", wt.Path);
        Assert(mok, "merge: " + mmsg);
        Assert((await File.ReadAllTextAsync(Path.Combine(tmp, "a.txt"))).StartsWith("feature2"), "main has merged content");

        await GitWorktree.RemoveAsync(tmp, wt.Path);
        Console.WriteLine("GitWorktree end-to-end OK (create/changes/diff/discard/commit-only/merge)");
    }
    finally
    {
        try { Directory.Delete(tmp, true); } catch { }
    }
}

static void AssertResumeArgs()
{
    var cwd = Environment.CurrentDirectory;
    var claude = new ClaudeAdapter().BuildStartInfo(
        "claude",
        new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "sess-1" },
        "next turn");
    var claudeArgs = claude.ArgumentList.ToArray();
    Assert(claudeArgs.Contains("--resume") && claudeArgs.Contains("sess-1"), "Claude resume args missing");

    var codex = new CodexAdapter().BuildStartInfo(
        "codex",
        new SessionOptions { WorkingDirectory = cwd, ResumeSessionId = "thread-1" },
        "next turn");
    var codexArgs = codex.ArgumentList.ToArray();
    Assert(codexArgs.Length >= 3 && codexArgs[0] == "exec" && codexArgs[1] == "resume" && codexArgs[2] == "thread-1",
        "Codex resume args missing");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
