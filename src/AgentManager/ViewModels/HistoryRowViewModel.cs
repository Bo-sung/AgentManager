using System.Globalization;
using AgentManager.Persistence;
using AgentManager.Core.Agents;

namespace AgentManager.ViewModels;

public sealed class HistoryRowViewModel
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public required string Badge { get; init; }
    public required string AgentName { get; init; }
    public required string Title { get; init; }
    public required string Project { get; init; }
    public required string Branch { get; init; }
    public required string Status { get; init; }
    public required string StatusLabel { get; init; }
    public required string Activity { get; init; }
    public required string StartedText { get; init; }
    public required string TokensText { get; init; }
    public required string CostText { get; init; }
    public required string BlocksText { get; init; }
    public bool IsArchived { get; init; }

    public static HistoryRowViewModel FromSession(SessionViewModel session)
    {
        return new HistoryRowViewModel
        {
            SessionId = session.Id,
            AgentId = session.AgentId,
            Badge = session.Badge,
            AgentName = session.AgentName,
            Title = string.IsNullOrWhiteSpace(session.Title) ? AgentManager.App.L("L.Untitled") : session.Title,
            Project = string.IsNullOrWhiteSpace(session.Project) ? AgentManager.App.L("L.NoProject") : session.Project,
            Branch = string.IsNullOrWhiteSpace(session.Branch) ? AgentManager.App.L("L.NoBranch") : session.Branch,
            Status = session.Status,
            StatusLabel = session.StatusLabel,
            Activity = session.Activity,
            StartedText = session.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            TokensText = $"{session.TokensIn:n0}/{session.TokensOut:n0}",
            CostText = session.CostUsd > 0 ? session.CostUsd.ToString("$0.000", CultureInfo.InvariantCulture) : "-",
            BlocksText = AgentManager.App.L("L.BlocksCount", session.Transcript.Count.ToString("n0", CultureInfo.CurrentCulture)),
            IsArchived = session.IsArchived,
        };
    }

    public static HistoryRowViewModel FromDto(SessionDto session)
    {
        var engine = EngineRegistry.Get(session.AgentId);
        var status = (session.Status ?? "").Trim().ToLowerInvariant();
        return new HistoryRowViewModel
        {
            SessionId = session.Id,
            AgentId = engine.Id,
            Badge = engine.Badge,
            AgentName = engine.Name,
            Title = string.IsNullOrWhiteSpace(session.Title) ? AgentManager.App.L("L.Untitled") : session.Title,
            Project = string.IsNullOrWhiteSpace(session.Project) ? AgentManager.App.L("L.NoProject") : session.Project,
            Branch = string.IsNullOrWhiteSpace(session.Branch) ? AgentManager.App.L("L.NoBranch") : session.Branch,
            Status = status,
            StatusLabel = status switch
            {
                "running" => AgentManager.App.L("L.StatusRunning"),
                "waiting" => AgentManager.App.L("L.StatusAwaitingInput"),
                "done" => AgentManager.App.L("L.StatusCompleted"),
                "error" => AgentManager.App.L("L.StatusFailed"),
                _ => AgentManager.App.L("L.StatusIdle"),
            },
            Activity = session.Activity,
            StartedText = session.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            TokensText = $"{session.TokensIn:n0}/{session.TokensOut:n0}",
            CostText = session.CostUsd > 0 ? session.CostUsd.ToString("$0.000", CultureInfo.InvariantCulture) : "-",
            BlocksText = AgentManager.App.L("L.BlocksCount", session.Transcript.Count.ToString("n0", CultureInfo.CurrentCulture)),
            IsArchived = session.IsArchived,
        };
    }

    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = filter.Trim();
        return Contains(Title, f) || Contains(Project, f) || Contains(Branch, f)
               || Contains(StatusLabel, f) || Contains(Activity, f) || Contains(AgentName, f);
    }

    private static bool Contains(string value, string filter)
        => value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
}
