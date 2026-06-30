using System.IO;
using AgentManager.Core.Agents;
using AgentManager.Core.Observation;

namespace AgentManager.Core.Orchestration;

/// <summary>Why a turn's engine could not be resolved. The frontend maps each value to a localized
/// error message — Core never holds UI strings (see the overhaul boundary: decisions → Core, rendering → frontend).</summary>
public enum EngineSetupError
{
    None,
    AgyPythonMissing,   // agy API mode but no python interpreter on PATH
    AgyBridgeMissing,   // agy API mode but the bundled SDK bridge .py is absent
    AgyKeyMissing,      // agy API mode but no GEMINI_API_KEY stored
    EngineUnavailable,  // adapter or executable could not be resolved
}

/// <summary>The engine resolved for a turn (adapter + executable), or a typed failure the frontend localizes.</summary>
public sealed record EngineResolution(IAgentAdapter? Adapter, string? Exe, EngineSetupError Error)
{
    public bool Ok => Error == EngineSetupError.None && Adapter is not null && Exe is not null;
    public static EngineResolution Fail(EngineSetupError error) => new(null, null, error);
}

/// <summary>Inputs to resolve a turn's engine. Serializable (no UI types) so it is also the future IPC
/// RunTurn request shape. <paramref name="ApiMode"/> is whether agy runs via the Antigravity SDK
/// (API key) rather than the CLI/ConPTY adapter; <paramref name="ResolvePython"/> lets the host pick the
/// interpreter (PATH probe today, a configured path later).</summary>
public sealed record EngineResolveRequest(
    string AgentId,
    bool RequireApproval,
    bool ApiMode,
    bool HasApiKey,
    string? ClaudePath,
    string? CodexPath,
    string? AgyPath,
    string? PiPath,
    Func<string?> ResolvePython);

/// <summary>Inputs to assemble a turn's <see cref="SessionOptions"/>. Pure data — a CLI frontend would
/// fill the same record. The spool dirs are created and pointed at via env; the native-hook command is
/// derived for engines that support it (gx/cc).</summary>
public sealed record TurnOptionsRequest(
    string AgentId,
    string WorkingDirectory,
    bool RequireApproval,
    SandboxMode Sandbox,
    string? ResumeSessionId,
    string? Model,
    string? McpConfigPath,
    IReadOnlyList<string> Images,
    string AttachedDocsText,
    IReadOnlyList<string> AdditionalDirectories,
    string? ReasoningEffort,
    IReadOnlyDictionary<string, string> ApiEnv,
    string TaskSpoolDir,
    string AskSpoolDir,
    string? NativeHookSpoolDirectory);

/// <summary>Headless turn-setup: the engine-resolution decision tree and the <see cref="SessionOptions"/>
/// assembly lifted out of the WPF run loop (<c>AppViewModel.Run.cs:RunTurnAsync</c>). No UI types, no
/// dispatcher — the GUI and a future CLI both set up a turn through this. The caller still owns the
/// running-turn registry, the worktree, transcript, and the <see cref="AgentSession"/> wiring; this only
/// decides the adapter/exe and builds the immutable options. (Overhaul (a) step 5, option-B slice:
/// extract the headless half of the run setup without inverting live-state ownership.)</summary>
public static class TurnPlanner
{
    /// <summary>Resolve the adapter + executable for a turn, or a typed failure. agy in API mode uses the
    /// SDK bridge (python) and pre-checks interpreter/bridge/key; every other case resolves through
    /// <see cref="EngineRegistry"/>. Mirrors the original branch in RunTurnAsync exactly.</summary>
    public static EngineResolution ResolveEngine(EngineResolveRequest r)
    {
        if (r.AgentId == "agy" && r.ApiMode)
        {
            var exe = r.ResolvePython();
            if (exe is null) return EngineResolution.Fail(EngineSetupError.AgyPythonMissing);
            if (!File.Exists(AgySdkAdapter.BridgeScriptPath)) return EngineResolution.Fail(EngineSetupError.AgyBridgeMissing);
            if (!r.HasApiKey) return EngineResolution.Fail(EngineSetupError.AgyKeyMissing);
            return new EngineResolution(new AgySdkAdapter(), exe, EngineSetupError.None);
        }

        var adapter = EngineRegistry.CreateAdapter(r.AgentId, r.RequireApproval);
        var exePath = EngineRegistry.ResolveExe(r.AgentId, r.ClaudePath, r.CodexPath, r.AgyPath, r.PiPath);
        return adapter is null || exePath is null
            ? EngineResolution.Fail(EngineSetupError.EngineUnavailable)
            : new EngineResolution(adapter, exePath, EngineSetupError.None);
    }

    /// <summary>Assemble the immutable <see cref="SessionOptions"/> for a turn. Creates the session's
    /// worker-task/ask spool dirs and points <c>AGENTMANAGER_TASK_SPOOL</c>/<c>AGENTMANAGER_ASK_SPOOL</c>
    /// at them (so skill-written tasks/asks are attributed to this session), and derives the native-hook
    /// command for gx/cc. Mirrors the original SessionOptions initializer + WithTaskSpoolEnv exactly.</summary>
    public static SessionOptions BuildOptions(TurnOptionsRequest r)
    {
        try { Directory.CreateDirectory(r.TaskSpoolDir); } catch { }
        try { Directory.CreateDirectory(r.AskSpoolDir); } catch { }
        var env = new Dictionary<string, string>(r.ApiEnv)
        {
            ["AGENTMANAGER_TASK_SPOOL"] = r.TaskSpoolDir,
            ["AGENTMANAGER_ASK_SPOOL"] = r.AskSpoolDir,
        };

        return new SessionOptions
        {
            WorkingDirectory = r.WorkingDirectory,
            BypassPermissions = !r.RequireApproval, // Stage 1: Claude stdio approvals; Codex falls to sandbox
            Sandbox = r.Sandbox,
            ResumeSessionId = r.ResumeSessionId,
            Model = string.IsNullOrWhiteSpace(r.Model) ? null : r.Model,
            McpConfigPath = string.IsNullOrWhiteSpace(r.McpConfigPath) ? null : r.McpConfigPath,
            Images = r.Images,
            AttachedDocsText = r.AttachedDocsText,
            AdditionalDirectories = r.AdditionalDirectories,
            ReasoningEffort = string.IsNullOrWhiteSpace(r.ReasoningEffort) ? null : r.ReasoningEffort,
            ExtraEnvironment = env,
            NativeHookSpoolDirectory = r.NativeHookSpoolDirectory,
            NativeHookCommand = r.AgentId is "gx" or "cc" && r.NativeHookSpoolDirectory is not null
                ? NativeHookCommandFactory.WindowsPowerShellSpoolScript(r.NativeHookSpoolDirectory)
                : null,
            BypassHookTrust = r.AgentId == "gx",
        };
    }
}
