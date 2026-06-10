// Headless smoke tests — adapter parsing/args (zero tokens) + GitWorktree
// end-to-end against a throwaway temp repo (zero tokens, real git).
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Session;
using AgentManager.Core.Workspace;

// Live approval round-trip (costs a few engine tokens): dotnet run -- --live-approval
if (args.Contains("--live-approval"))
{
    await LiveApprovalAsync();
    return;
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
    Console.WriteLine("sandbox/model/mcp arg asserts OK");
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
