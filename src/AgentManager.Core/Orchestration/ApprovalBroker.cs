using AgentManager.Core.Agents;

namespace AgentManager.Core.Orchestration;

/// <summary>The permission round-trip, headless. Owns the pending <see cref="PermissionDecision"/>
/// completions keyed by request id; the engine awaits <see cref="Request"/>, and whichever frontend is
/// attached answers via <see cref="Resolve"/>. No UI types cross this — the frontend renders the request
/// (the GUI an ApprovalBlock, a CLI a stdin prompt) and maps request ids to its own view state. This is
/// what lets a headless core / daemon never block on a GUI dialog (overhaul rule §6.5; step 5). The
/// session→requests mapping (for expiry on turn end) stays with the frontend, which knows its sessions.
/// Single-threaded by contract (owner/UI thread), like the original broker.</summary>
public sealed class ApprovalBroker
{
    private readonly Dictionary<string, TaskCompletionSource<PermissionDecision>> _pending = new();

    /// <summary>Register a pending approval and return the task the engine awaits. The caller (which holds
    /// the PermissionRequest details) is responsible for surfacing it to the user.</summary>
    public Task<PermissionDecision> Request(string requestId)
    {
        var tcs = new TaskCompletionSource<PermissionDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;
        return tcs.Task;
    }

    public bool IsPending(string requestId) => _pending.ContainsKey(requestId);

    /// <summary>Complete an approval with the user's decision. Returns false if it was already resolved or
    /// is unknown (a late/duplicate click).</summary>
    public bool Resolve(string requestId, PermissionDecision decision)
    {
        if (!_pending.Remove(requestId, out var tcs)) return false;
        tcs.TrySetResult(decision);
        return true;
    }
}
