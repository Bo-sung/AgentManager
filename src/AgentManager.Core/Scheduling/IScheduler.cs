using System;

namespace AgentManager.Core.Scheduling;

public class ScheduleDueEventArgs : EventArgs
{
    public ScheduledJob Job { get; }

    public ScheduleDueEventArgs(ScheduledJob job)
    {
        Job = job;
    }
}

public interface IScheduler
{
    event EventHandler<ScheduleDueEventArgs>? JobDue;
    void Start();
    void Stop();
}
