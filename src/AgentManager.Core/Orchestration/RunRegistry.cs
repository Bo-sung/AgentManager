namespace AgentManager.Core.Orchestration;

/// <summary>The running-turn registry — owns the per-session <see cref="CancellationTokenSource"/> for an
/// in-flight turn. Lifted out of the WPF VM's <c>_running</c> dict so the turn-loop ownership can move to
/// Core: a closed GUI window (phase b) or a CLI drives the same registry. Cancelling a turn fires the
/// engine's process-tree kill (the adapter wires the token to <c>proc.Kill(entireProcessTree: true)</c>).
/// Single-threaded by contract (called on the owner/UI thread), like the original. Overhaul (a) step 5.</summary>
public sealed class RunRegistry
{
    private readonly Dictionary<string, CancellationTokenSource> _running = new();

    public bool IsRunning(string sessionId) => _running.ContainsKey(sessionId);
    public int Count => _running.Count;

    /// <summary>Begin tracking a turn for a session and return its cancellation token. A session runs one
    /// turn at a time; any stale prior CTS is disposed first (defensive — the normal flow Completes first).</summary>
    public CancellationToken Start(string sessionId)
    {
        if (_running.Remove(sessionId, out var prior)) prior.Dispose();
        var cts = new CancellationTokenSource();
        _running[sessionId] = cts;
        return cts.Token;
    }

    /// <summary>Finish a turn — remove and dispose its CTS. No-op if not tracked.</summary>
    public void Complete(string sessionId)
    {
        if (_running.Remove(sessionId, out var cts)) cts.Dispose();
    }

    /// <summary>Request cancellation of a session's turn (fires the process-tree kill). No-op if not running.</summary>
    public void Cancel(string sessionId)
    {
        if (_running.TryGetValue(sessionId, out var cts)) { try { cts.Cancel(); } catch { } }
    }

    /// <summary>Cancel every in-flight turn (window close / shutdown). Non-blocking — fire only, never wait.</summary>
    public void CancelAll()
    {
        foreach (var cts in _running.Values.ToArray()) { try { cts.Cancel(); } catch { } }
    }
}
