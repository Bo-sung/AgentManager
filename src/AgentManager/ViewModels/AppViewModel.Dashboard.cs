using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AgentManager.Persistence;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Observation;
using AgentManager.Core.Scheduling;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;
using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    // ----- counts -----
    public int RunningCount => CountBy("running");
    public int WaitingCount => CountBy("waiting");
    public int DoneCount => CountBy("done");
    public int FailedCount => CountBy("error");
    private int CountBy(string s) { int n = 0; foreach (var x in Sessions) if (x.Status == s) n++; return n; }
    private void RefreshCounts()
    {
        OnChanged(nameof(RunningCount));
        OnChanged(nameof(WaitingCount));
        OnChanged(nameof(DoneCount));
        OnChanged(nameof(FailedCount));
        OnChanged(nameof(FleetThroughputLabel));
    }

    // ----- aggregate dashboard (all sessions) -----
    public string TotalTokensLabel
    {
        get
        {
            long tin = 0, tout = 0;
            foreach (var x in _allSessions) { tin += x.TokensIn; tout += x.TokensOut; }
            return $"{FmtK(tin)} / {FmtK(tout)}";
        }
    }
    public string TotalCostLabel
    {
        get
        {
            double c = 0;
            foreach (var x in _allSessions) c += x.CostUsd;
            return c > 0 ? "$" + c.ToString("0.00") : "$0";
        }
    }
    private void RefreshTotals()
    {
        OnChanged(nameof(TotalTokensLabel));
        OnChanged(nameof(TotalCostLabel));
        OnChanged(nameof(FleetThroughputLabel));
    }
    public string FleetThroughputLabel
    {
        get
        {
            long total = 0;
            foreach (var x in _allSessions)
                total += x.TokensIn + x.TokensOut;
            return FmtK(total) + " tok";
        }
    }
    private static string FmtK(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.0") + "M"
        : n >= 1000 ? (n / 1000.0).ToString("0.0") + "k" : n.ToString();

    private void RefreshRunningSessions()
    {
        foreach (var session in _allSessions)
            if (session.IsRunning)
                session.RefreshRuntimeLabels();

        OnChanged(nameof(FleetThroughputLabel));

        // live review: pick up files a still-running tool is writing (debounced inside)
        if (ActiveSession is { IsRunning: true } run)
            _ = QueueLiveReviewRefreshAsync(run);
    }

}
