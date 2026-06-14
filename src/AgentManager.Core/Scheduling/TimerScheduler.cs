using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("AgentManager.Smoke")]

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

            // 반드시 백그라운드 스레드에서 구동: UI 스레드에서 시작하면 PeriodicTimer continuation이
            // UI SynchronizationContext에 묶여, Dispose→Stop의 GetResult()(UI 블록)와 상호 대기
            // 데드락이 발생해 창이 닫히지 않는다.
            _timerTask = Task.Run(() => RunTimerAsync(_cts.Token));
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

    internal void EvaluateJobs()
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

        var jobsToTrigger = new List<ScheduledJob>();

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
                    jobsToTrigger.Add(updated);
                }
                else
                {
                    updatedJobs.Add(job);
                }
            }

            _jobs = updatedJobs;
            ScheduleStore.Save(_jobs);
        }

        foreach (var jobToTrigger in jobsToTrigger)
        {
            try
            {
                JobDue?.Invoke(this, new ScheduleDueEventArgs(jobToTrigger));
            }
            catch
            {
                // Prevent event handler exception from breaking the scheduler loop
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        Stop();
        _isDisposed = true;
    }
}
