// Offline parser smoke test — feeds captured JSONL samples through the adapters
// and prints the normalized events. No CLI/model calls (zero tokens).
using AgentManager.Core.Agents;
using AgentManager.Core.Events;

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
Console.WriteLine("smoke OK");

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
