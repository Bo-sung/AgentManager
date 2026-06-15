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
    // ----- new-schedule overlay state -----
    private bool _showNewSchedule;
    public bool ShowNewSchedule { get => _showNewSchedule; set => Set(ref _showNewSchedule, value); }
    private EngineDef? _newScheduleEngine;
    public EngineDef? NewScheduleSelectedEngine { get => _newScheduleEngine; set => Set(ref _newScheduleEngine, value); }
    private string _newScheduleTitle = "";
    public string NewScheduleTitle { get => _newScheduleTitle; set => Set(ref _newScheduleTitle, value); }
    private string _newSchedulePrompt = "";
    public string NewSchedulePrompt { get => _newSchedulePrompt; set => Set(ref _newSchedulePrompt, value); }
    private string _newScheduleCadence = "";
    public string NewScheduleCadence { get => _newScheduleCadence; set => Set(ref _newScheduleCadence, value); }
    private string _newScheduleTargetBranch = "";
    public string NewScheduleTargetBranch { get => _newScheduleTargetBranch; set => Set(ref _newScheduleTargetBranch, value); }
    private string _newScheduleError = "";
    public string NewScheduleError { get => _newScheduleError; set => Set(ref _newScheduleError, value); }

    private void LoadScheduledJobs()
    {
        ScheduledJobs.Clear();
        foreach (var job in ScheduleStore.Load())
            ScheduledJobs.Add(new ScheduledJobViewModel(job));
        _scheduler.Reload();
    }

    private void OpenNewSchedule()
    {
        NewScheduleSelectedEngine = Engines.FirstOrDefault() ?? EngineRegistry.All[0];
        NewScheduleTitle = "";
        NewSchedulePrompt = "";
        NewScheduleCadence = "Every day · 02:00";
        NewScheduleTargetBranch = "agent/scheduled-task";
        NewScheduleError = "";
        ShowNewSchedule = true;
    }

    private void CreateSchedule()
    {
        var project = ActiveProject;
        if (project is null) return;

        var engine = NewScheduleSelectedEngine ?? Engines.FirstOrDefault() ?? EngineRegistry.All[0];
        var title = NewScheduleTitle.Trim();
        var prompt = string.IsNullOrWhiteSpace(NewSchedulePrompt) ? title : NewSchedulePrompt.Trim();
        var cadence = string.IsNullOrWhiteSpace(NewScheduleCadence) ? "Every day · 02:00" : NewScheduleCadence.Trim();
        var branch = string.IsNullOrWhiteSpace(NewScheduleTargetBranch) ? "agent/" + Slug(title) : NewScheduleTargetBranch.Trim();
        var isEvent = cadence.StartsWith("on push", StringComparison.OrdinalIgnoreCase) || cadence.StartsWith("On push", StringComparison.OrdinalIgnoreCase);
        var cron = ScheduleTrigger.TryParseCadenceToCron(cadence);
        if (!isEvent && string.IsNullOrWhiteSpace(cron))
        {
            NewScheduleError = L("L.SchedInvalidCadence");
            return;
        }

        var job = new ScheduledJob
        {
            Id = "job" + DateTime.Now.Ticks,
            AgentId = engine.Id,
            ProjectId = project.Id,
            ProjectPath = project.Path,
            Title = title,
            Prompt = prompt,
            TargetBranch = branch,
            Trigger = new ScheduleTrigger
            {
                Kind = isEvent ? "Event" : "Cron",
                CadenceText = cadence,
                CronExpression = isEvent ? null : cron,
                TargetPath = isEvent ? cadence.Replace("On push to", "", StringComparison.OrdinalIgnoreCase).Trim() : null,
            },
        };

        var jobs = ScheduleStore.Load();
        jobs.Insert(0, job);
        ScheduleStore.Save(jobs);
        ShowNewSchedule = false;
        LoadScheduledJobs();
    }

    private void Scheduler_JobDue(object? sender, ScheduleDueEventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() => RunScheduledJob(e.Job));
    }

    private void RunScheduledJob(ScheduledJob job)
    {
        var project = !string.IsNullOrWhiteSpace(job.ProjectId)
            ? Projects.FirstOrDefault(p => p.Id == job.ProjectId)
            : null;
        if (project is null && !string.IsNullOrWhiteSpace(job.ProjectPath) && Directory.Exists(job.ProjectPath))
        {
            project = Projects.FirstOrDefault(p => string.Equals(p.Path, job.ProjectPath, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                project = new ProjectViewModel("scheduled-" + DateTime.Now.Ticks, new DirectoryInfo(job.ProjectPath).Name, job.ProjectPath);
                Projects.Add(project);
            }
        }
        project ??= ActiveProject ?? Projects.FirstOrDefault();
        if (project is null) return;

        var engine = EngineRegistry.Get(job.AgentId);
        var title = string.IsNullOrWhiteSpace(job.Title) ? L("L.ScheduledDefaultTitle") : job.Title.Trim();
        var branch = string.IsNullOrWhiteSpace(job.TargetBranch) ? "agent/" + Slug(title) : job.TargetBranch.Trim();
        var prompt = string.IsNullOrWhiteSpace(job.Prompt) ? title : job.Prompt.Trim();

        var session = new SessionViewModel("s" + DateTime.Now.Ticks, engine, title, branch,
            project.Id, project.Name, project.Path, engine.Models[0])
        {
            TranslationEnabled = TranslationEnabled,
            Activity = L("L.ScheduledQueued"),
        };
        session.Transcript.Add(new WorkingBlock(L("L.ScheduledRunMarker", job.Trigger.CadenceText)));
        session.PropertyChanged += SessionStatusWatch;
        _allSessions.Insert(0, session);
        ActiveSession = session;
        RefreshProjectSessions(selectFirstIfMissing: false);
        RefreshCounts();
        RefreshProjectCounts();
        LoadScheduledJobs();
        SaveState();

        _ = RunTurnAsync(session, prompt);
    }

}
