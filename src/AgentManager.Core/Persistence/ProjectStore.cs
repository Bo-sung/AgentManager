namespace AgentManager.Core.Persistence;

/// <summary>Headless debounced state-save coordinator — owns the *mechanism* of persistence: the
/// coalescing debounce timer (with a max-latency cap so a busy turn still saves within ~1s), the
/// "snapshot live state on the owner thread, write the immutable snapshot off-thread" split, and the
/// background-write hand-off. The frontend supplies HOW to snapshot its live state (<c>build</c>) and
/// HOW to persist a snapshot (<c>write</c>); a GUI and a CLI differ only in those two delegates. No WPF
/// or UI types here — the owner-thread marshal and the "can we debounce" check are injected so Core
/// stays portable (the live model that <c>build</c> reads is UI-affine, hence it must run on the owner
/// thread; the resulting snapshot is immutable, hence the write can run anywhere).</summary>
/// <typeparam name="TSnapshot">An immutable snapshot the frontend builds from its live state and the
/// writer persists. This coordinator only passes it from build to write — it never inspects it.</typeparam>
public sealed class ProjectStore<TSnapshot> : IDisposable
{
    private readonly Func<TSnapshot> _build;          // snapshot live state → immutable DTO (owner thread)
    private readonly Func<TSnapshot, bool> _write;    // persist snapshot; returns whether the global write succeeded (any thread)
    private readonly Action<Action> _postToOwner;     // marshal an action onto the owner (UI) thread; inline if none
    private readonly Action<bool> _report;            // report write ok/fail (drives a non-blocking UI banner)
    private readonly Action<string, Exception> _logError;
    private readonly Func<bool> _canDebounce;         // false (no dispatcher: tests/shutdown) → flush immediately
    private readonly TimeSpan _debounce, _maxLatency;

    private readonly object _gate = new();
    private Timer? _timer;
    private DateTime _firstPendingUtc;
    private bool _pending, _disposed;

    public ProjectStore(
        Func<TSnapshot> build, Func<TSnapshot, bool> write, Action<Action> postToOwner,
        Action<bool> report, Action<string, Exception> logError, Func<bool> canDebounce,
        TimeSpan debounce, TimeSpan maxLatency)
    {
        _build = build; _write = write; _postToOwner = postToOwner;
        _report = report; _logError = logError; _canDebounce = canDebounce;
        _debounce = debounce; _maxLatency = maxLatency;
    }

    /// <summary>Coalescing save request — call on the owner thread. Restarts the debounce; once the first
    /// pending change is older than the max-latency cap it flushes now, so saves never starve under
    /// continuous activity. With no owner-thread dispatcher (tests/shutdown) it flushes immediately.</summary>
    public void Save()
    {
        if (!_canDebounce()) { Flush(synchronousWrite: false); return; }
        var flushNow = false;
        lock (_gate)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            if (!_pending) { _pending = true; _firstPendingUtc = now; }
            if (now - _firstPendingUtc >= _maxLatency) flushNow = true;
            else
            {
                _timer?.Dispose();
                _timer = new Timer(_ => OnTick(), null, _debounce, Timeout.InfiniteTimeSpan);
            }
        }
        if (flushNow) Flush(synchronousWrite: false); // cap reached → don't defer further (cancels pending inside)
    }

    private void OnTick()
    {
        lock (_gate) { if (_disposed) return; }
        // The debounce timer fires on a thread-pool thread; the build reads UI-affine state, so hop to the
        // owner thread to build (and from there the write goes back to the background).
        _postToOwner(() => Flush(synchronousWrite: false));
    }

    /// <summary>Build the snapshot on the calling (owner) thread, then persist it — synchronously inline
    /// (shutdown: the process may die before a background task finishes) or on a background task. Any
    /// pending debounce is cancelled. Never throws — a failed save must not break the run or the UI.</summary>
    public void Flush(bool synchronousWrite)
    {
        ClearPending();
        TSnapshot snap;
        try { snap = _build(); }
        catch (Exception ex) { _logError("BuildStateDto", ex); _report(false); return; }
        if (synchronousWrite) _report(_write(snap));
        else Task.Run(() => { var ok = _write(snap); _postToOwner(() => _report(ok)); });
    }

    private void ClearPending()
    {
        lock (_gate) { _pending = false; _timer?.Dispose(); _timer = null; }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _timer?.Dispose(); _timer = null; }
    }
}
