using AgentManager.Core.Events;

namespace AgentManager.Core.Usage;

/// <summary>A captured rate-limit/usage reading for one engine. <c>Utilization</c>/<c>WeekUtilization</c>
/// are 0..1 (used fraction), -1 = unknown.
///   cc: session/week % come only from the <c>/usage</c> command text (rate_limit_event carries reset only).
///   gx: app-server account/rateLimits usedPercent is the real usage.</summary>
public sealed record UsageSnapshot(double Utilization, long ResetsAtUnix, string RateLimitType, DateTime CapturedUtc, double WeekUtilization = -1);

/// <summary>Owns the per-engine usage snapshots and the capture/selection rules — headless (no WPF). The
/// frontend formats snapshots into rows/footer text (localized); this only holds the data and the domain
/// logic for merging captures and picking which engine to show. (Overhaul (a) step 6, option-B slice.)</summary>
public sealed class UsageService
{
    private readonly Dictionary<string, UsageSnapshot> _usage = new();

    /// <summary>The current snapshots, for display projection and persistence mapping.</summary>
    public IReadOnlyDictionary<string, UsageSnapshot> Snapshots => _usage;

    public bool TryGet(string engineId, out UsageSnapshot snapshot) => _usage.TryGetValue(engineId, out snapshot!);

    /// <summary>Authoritative capture (explicit probe / restored state) — replaces whatever was there.</summary>
    public void Set(string engineId, UsageSnapshot snapshot) => _usage[engineId] = snapshot;

    public void Clear() => _usage.Clear();

    /// <summary>Passive capture during a run. A real reading (util>=0) replaces the snapshot (keeping the
    /// previous week %); cc's reset-only event (util&lt;0) updates just the reset time/type and keeps the
    /// existing % and capture time.</summary>
    public void Record(string engineId, QuotaUpdate q)
    {
        _usage.TryGetValue(engineId, out var prev);
        if (q.Utilization >= 0)
            _usage[engineId] = new UsageSnapshot(q.Utilization, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow, prev?.WeekUtilization ?? -1);
        else if (prev is not null)
            _usage[engineId] = prev with { ResetsAtUnix = q.ResetsAtUnix, RateLimitType = q.RateLimitType };
        else
            _usage[engineId] = new UsageSnapshot(-1, q.ResetsAtUnix, q.RateLimitType, DateTime.UtcNow);
    }

    /// <summary>Which engine's usage to surface (footer): the active session's engine if it has a snapshot,
    /// otherwise the most-recently-captured engine. Returns (null, null) when nothing has been captured.</summary>
    public (string? EngineId, UsageSnapshot? Snapshot) PickDisplay(string? activeEngineId)
    {
        if (activeEngineId is not null && _usage.TryGetValue(activeEngineId, out var active))
            return (activeEngineId, active);

        string? bestId = null;
        UsageSnapshot? best = null;
        foreach (var pair in _usage)
            if (best is null || pair.Value.CapturedUtc > best.CapturedUtc)
            {
                bestId = pair.Key;
                best = pair.Value;
            }
        return (bestId, best);
    }
}
