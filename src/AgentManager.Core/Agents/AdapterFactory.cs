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
        // Custom-engine adapter kinds (one-shot-text / agentmanager-bridge-jsonl) are added in P4.
        _ => null,
    };
}
