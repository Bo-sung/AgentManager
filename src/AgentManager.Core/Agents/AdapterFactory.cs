namespace AgentManager.Core.Agents;

/// <summary>
/// Creates an <see cref="IAgentAdapter"/> from an <b>adapter kind</b> (protocol) rather than a hardcoded engine id.
/// This decouples the engine identity from its protocol so custom engines (P4) can declare a built-in adapter kind
/// in their manifest. Built-in engines resolve their kind via <see cref="EngineRegistry.BuiltinAdapterKind"/>.
/// </summary>
public static class AdapterFactory
{
    /// <summary>Instantiate the adapter for a protocol kind, or null if unknown/unsupported.</summary>
    public static IAgentAdapter? Create(string? adapterKind, bool requireApproval = false) => adapterKind switch
    {
        "claude-stream-json" => new ClaudeAdapter(),
        // codex has two protocols: the plain exec --json, and the app-server approval-gated path (Stage 2).
        "codex-json" => requireApproval ? new CodexAppServerAdapter() : new CodexAdapter(),
        "codex-app-server" => new CodexAppServerAdapter(),
        "agy-pty" => new AgyAdapter(),
        "pi-rpc" => new PiAdapter(),
        // Custom-engine-only kinds need launch args → use CreateCustom.
        _ => null,
    };

    /// <summary>Create the adapter for a CUSTOM engine from its manifest. One-shot-text engines need the launch
    /// argument template; a custom engine may also reuse a built-in protocol kind (then args are ignored).</summary>
    public static IAgentAdapter? CreateCustom(string engineId, string? adapterKind, IReadOnlyList<string> argsTemplate, bool requireApproval = false) => adapterKind switch
    {
        "one-shot-text" => new OneShotTextAdapter(engineId, argsTemplate),
        // Richer JSONL protocol (tool-calls/thinking/usage). Accept both spellings — the canonical
        // manifest value is "agentmanager-bridge-jsonl"; "bridge-jsonl" is the shorthand alias.
        "agentmanager-bridge-jsonl" or "bridge-jsonl" => new BridgeJsonlAdapter(engineId, argsTemplate),
        // Agent Client Protocol (Zed's JSON-RPC/stdio) — opencode `acp`, hermes `hermes-acp`, etc. The launch
        // subcommand comes from launch.args (e.g. ["acp"]); the prompt is delivered via session/prompt, not args.
        "acp" => new AcpAdapter(engineId, argsTemplate),
        _ => Create(adapterKind, requireApproval), // custom engine reusing a built-in protocol
    };
}
