using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using AgentManager.Core.Workers;

namespace AgentManager.ViewModels;

/// <summary>
/// Worker report skill — a worker writes its final report to <c>&lt;cwd&gt;/.am/report/&lt;workerSessionId&gt;/report.md</c>
/// (path handed to it via the <c>AGENTMANAGER_REPORT_SPOOL</c> env var). This watches that directory, attaches the
/// text to the worker's currently-running task, and marks it Done → the report lands in the origin (control-tower)
/// session's report inbox. This is DECOUPLED from turn-completion detection: once the worker sends the report the
/// task is finalized, so even if the turn later stalls/hangs the report is already delivered. The completion-based
/// capture in <see cref="AppViewModel"/>'s worker runner remains as a fallback for a worker that never sends one
/// (the runner's Running-status guard skips it when the report already arrived here).
/// </summary>
public partial class AppViewModel
{
    private readonly HashSet<string> _watchedReportDirs = [];
    private readonly List<FileSystemWatcher> _reportWatchers = [];

    /// <summary>Watch a worker session's <c>&lt;cwd&gt;/.am/report/&lt;sessionId&gt;/</c>. Idempotent per directory.</summary>
    private void WatchSessionReportSpool(string cwd, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return;
        try
        {
            var dir = Path.Combine(cwd, ".am", "report", sessionId);
            Directory.CreateDirectory(dir);
            if (!_watchedReportDirs.Add(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*.md")) ScheduleReportIngest(f, sessionId);
            var w = new FileSystemWatcher(dir, "*.md")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Created += (_, e) => ScheduleReportIngest(e.FullPath, sessionId);
            w.Changed += (_, e) => ScheduleReportIngest(e.FullPath, sessionId);
            w.Renamed += (_, e) => ScheduleReportIngest(e.FullPath, sessionId);
            _reportWatchers.Add(w); // keep alive for the app's lifetime
        }
        catch { /* best-effort */ }
    }

    private void ScheduleReportIngest(string path, string sessionId) =>
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await System.Threading.Tasks.Task.Delay(150); // FS event may fire mid-write
            IngestReportFile(path, sessionId);
        });

    private void IngestReportFile(string path, string workerSessionId)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (Core.JsonFile.ReadCapped(path) is not { } text) return; // missing/oversized (SEC: spool DoS guard)
            text = text.Trim();
            if (text.Length == 0) return;

            // Attach to this worker's currently-running task → files into the origin's report inbox + marks Done.
            var running = _taskStore.QueueFor(workerSessionId).FirstOrDefault(t => t.Status == WorkerTaskStatus.Running);
            if (running is not null)
            {
                _taskStore.SetReport(running.Id, text);
                _taskStore.SetStatus(running.Id, WorkerTaskStatus.Done);
            }
            try { File.Delete(path); } catch { }
        }
        catch { }
    }
}
