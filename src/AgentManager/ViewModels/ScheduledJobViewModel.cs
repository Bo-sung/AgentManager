using AgentManager.Core.Agents;
using AgentManager.Core.Scheduling;

namespace AgentManager.ViewModels;

public sealed class ScheduledJobViewModel(ScheduledJob job)
{
    public ScheduledJob Job { get; } = job;
    public string Id => Job.Id;
    public string AgentId => Job.AgentId;
    public string Title => Job.Title;
    public string Prompt => Job.Prompt;
    public string TargetBranch => Job.TargetBranch;
    public string CadenceText => Job.Trigger.CadenceText;
    public string TriggerKind => Job.Trigger.Kind;
    public bool Enabled => Job.Enabled;
    public string EngineName => Resolve().Name;
    public string EngineBadge => Resolve().Badge;
    public string NextRunLabel => Job.NextRunUtc is { } next ? FormatNextRun(next) : "on trigger";

    /// <summary>Resolves the job's AgentId (incl. custom engines) to its <see cref="EngineDef"/>. Set once by
    /// AppViewModel to its custom-aware resolver; falls back to <see cref="EngineRegistry.Get"/> (cc for an
    /// unknown/custom id) when unset — i.e. at design-time or in headless Smoke tests.</summary>
    public static Func<string, EngineDef>? EngineResolver;
    private EngineDef Resolve() => EngineResolver?.Invoke(Job.AgentId) ?? EngineRegistry.Get(Job.AgentId);

    private static string FormatNextRun(DateTime nextUtc)
    {
        var span = nextUtc - DateTime.UtcNow;
        if (span <= TimeSpan.Zero) return "due now";
        if (span.TotalDays >= 1) return "in " + Math.Ceiling(span.TotalDays).ToString("0") + "d";
        if (span.TotalHours >= 1) return "in " + Math.Ceiling(span.TotalHours).ToString("0") + "h";
        return "in " + Math.Max(1, Math.Ceiling(span.TotalMinutes)).ToString("0") + "m";
    }
}
