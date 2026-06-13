using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentManager.Core.Scheduling;

public sealed class TimerScheduler : IScheduler, IDisposable
{
    public event EventHandler<ScheduleDueEventArgs>? JobDue;

    private readonly object _lock = new();
    private List<ScheduledJob> _jobs = [];
    private CancellationTokenSource? _cts;
    private Task? _timerTask;
    private bool _isDisposed;

    public void Start()
    {
        lock (_lock)
        {
            if (_timerTask != null) return; // Already running

            _cts = new CancellationTokenSource();
            _jobs = ScheduleStore.Load();

            _timerTask = RunTimerAsync(_cts.Token);
        }
    }

    public void Stop()
    {
        CancellationTokenSource? ctsToCancel = null;
        Task? taskToWait = null;

        lock (_lock)
        {
            if (_timerTask == null) return;

            ctsToCancel = _cts;
            taskToWait = _timerTask;

            _cts = null;
            _timerTask = null;
        }

        if (ctsToCancel != null)
        {
            ctsToCancel.Cancel();
            try
            {
                taskToWait?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            finally
            {
                ctsToCancel.Dispose();
            }
        }
    }

    public void Reload()
    {
        lock (_lock)
        {
            _jobs = ScheduleStore.Load();
        }
    }

    private async Task RunTimerAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(ct))
        {
            EvaluateJobs();
        }
    }

    private void EvaluateJobs()
    {
        List<ScheduledJob> jobsToEvaluate;
        lock (_lock)
        {
            jobsToEvaluate = new List<ScheduledJob>(_jobs);
        }

        var now = DateTime.UtcNow;
        var dueJobs = new List<ScheduledJob>();

        foreach (var job in jobsToEvaluate)
        {
            if (job.Enabled && job.NextRunUtc.HasValue && job.NextRunUtc.Value <= now)
            {
                dueJobs.Add(job);
            }
        }

        if (dueJobs.Count == 0) return;

        lock (_lock)
        {
            var updatedJobs = new List<ScheduledJob>();
            foreach (var job in _jobs)
            {
                var dueJob = dueJobs.Find(dj => dj.Id == job.Id);
                if (dueJob != null)
                {
                    var updated = job with { LastRunUtc = DateTime.UtcNow };
                    updatedJobs.Add(updated);

                    try
                    {
                        JobDue?.Invoke(this, new ScheduleDueEventArgs(updated));
                    }
                    catch
                    {
                        // Prevent event handler exception from breaking the scheduler loop
                    }
                }
                else
                {
                    updatedJobs.Add(job);
                }
            }

            _jobs = updatedJobs;
            ScheduleStore.Save(_jobs);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        Stop();
        _isDisposed = true;
    }
}
