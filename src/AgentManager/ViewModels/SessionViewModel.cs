using System.Collections.ObjectModel;

namespace AgentManager.ViewModels;

public sealed class SessionViewModel : ObservableObject
{
    public string Id { get; }
    public string AgentId { get; }
    public string Badge { get; }
    public string AgentName { get; }
    public string Cli { get; }
    public string Branch { get; }
    public string ProjectId { get; }
    public string Project { get; }
    public string ProjectPath { get; }
    public DateTime StartedAt { get; }
    public ObservableCollection<TranscriptItem> Transcript { get; } = [];
    public ObservableCollection<ReviewChangeViewModel> Changes { get; } = [];

    /// <summary>Per-session git worktree (isolation). Null = ran directly (non-git folder).</summary>
    private string? _worktreePath;
    public string? WorktreePath
    {
        get => _worktreePath;
        set { if (Set(ref _worktreePath, value)) OnChanged(nameof(WorktreeLabel)); }
    }

    private bool _isolated;
    public bool Isolated
    {
        get => _isolated;
        set { if (Set(ref _isolated, value)) OnChanged(nameof(WorktreeLabel)); }
    }
    public bool WorktreeAttempted { get; set; }

    public SessionViewModel(
        string id,
        EngineDef engine,
        string title,
        string branch,
        string projectId,
        string project,
        string projectPath,
        string model,
        DateTime? startedAt = null)
    {
        Id = id; AgentId = engine.Id; Badge = engine.Badge; AgentName = engine.Name; Cli = engine.Cli;
        _title = title; Branch = branch; ProjectId = projectId; Project = project; ProjectPath = projectPath; _model = model;
        StartedAt = startedAt ?? DateTime.Now;
    }

    private string _title;
    public string Title { get => _title; set => Set(ref _title, value); }

    /// <summary>Archived sessions are hidden from the Active/Project groups (kept in storage).</summary>
    private bool _isArchived;
    public bool IsArchived { get => _isArchived; set => Set(ref _isArchived, value); }

    private string _status = "idle";
    public string Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                OnChanged(nameof(StatusLabel));
                OnChanged(nameof(IsRunning));
                OnChanged(nameof(IsLive));
                OnChanged(nameof(CanSend));
                RefreshRuntimeLabels();
            }
        }
    }
    public string StatusLabel => _status switch
    {
        "running" => "Running", "waiting" => "Awaiting input", "done" => "Completed", "error" => "Failed", _ => "Idle"
    };
    public bool IsRunning => _status == "running";
    public bool IsLive => _status is "running" or "waiting";

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private string _model;
    public string Model { get => _model; set => Set(ref _model, value); }

    private bool _translationEnabled = true;
    public bool TranslationEnabled
    {
        get => _translationEnabled;
        set
        {
            if (Set(ref _translationEnabled, value))
                OnChanged(nameof(TranslationLabel));
        }
    }
    public string TranslationLabel => TranslationEnabled ? "TR ON" : "TR OFF";

    private string? _engineSessionId;
    public string? EngineSessionId
    {
        get => _engineSessionId;
        set => Set(ref _engineSessionId, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    private string _activity = "";
    public string Activity
    {
        get => _activity;
        set
        {
            if (Set(ref _activity, value))
                OnChanged(nameof(BusyLine));
        }
    }

    private DateTime? _runStartedAt;
    private DateTime? _lastSignalAt;

    public string BusyLine
    {
        get
        {
            if (!IsRunning) return string.IsNullOrWhiteSpace(Activity) ? StatusLabel : Activity;
            var activity = string.IsNullOrWhiteSpace(Activity) ? "waiting for engine output" : Activity;
            return IsQuiet ? $"{activity} · no output for {Ago(_lastSignalAt ?? _runStartedAt ?? DateTime.Now)}" : activity;
        }
    }

    public string RunningElapsedLabel => IsRunning && _runStartedAt is { } started ? FormatDuration(DateTime.Now - started) : "";
    public string LastSignalLabel => IsRunning && _lastSignalAt is { } last ? "last signal " + Ago(last) + " ago" : "waiting for first signal";
    public bool IsQuiet => IsRunning && _lastSignalAt is { } last && DateTime.Now - last > TimeSpan.FromSeconds(45);

    public void MarkRunStarted(string activity)
    {
        _runStartedAt = DateTime.Now;
        _lastSignalAt = null;
        Activity = activity;
        RefreshRuntimeLabels();
    }

    public void MarkRunSignal(string? activity = null)
    {
        _lastSignalAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(activity))
            Activity = activity;
        RefreshRuntimeLabels();
    }

    public void MarkRunEnded(string activity)
    {
        Activity = activity;
        RefreshRuntimeLabels();
    }

    public void RefreshRuntimeLabels()
    {
        OnChanged(nameof(BusyLine));
        OnChanged(nameof(RunningElapsedLabel));
        OnChanged(nameof(LastSignalLabel));
        OnChanged(nameof(IsQuiet));
    }

    private ReviewChangeViewModel? _selectedChange;
    public ReviewChangeViewModel? SelectedChange
    {
        get => _selectedChange;
        set => Set(ref _selectedChange, value);
    }

    private string _diffText = "변경 파일을 선택하면 diff가 표시됩니다.";
    public string DiffText { get => _diffText; set => Set(ref _diffText, value); }

    private string _reviewStatus = "No isolated worktree yet";
    public string ReviewStatus { get => _reviewStatus; set => Set(ref _reviewStatus, value); }

    public string WorktreeLabel => Isolated && WorktreePath is not null ? WorktreePath : "not isolated";

    private long _tokensIn, _tokensOut;
    public long TokensIn { get => _tokensIn; set { if (Set(ref _tokensIn, value)) OnChanged(nameof(TokensLabel)); } }
    public long TokensOut { get => _tokensOut; set { if (Set(ref _tokensOut, value)) OnChanged(nameof(TokensLabel)); } }
    public string TokensLabel => $"{Fmt(_tokensIn)} / {Fmt(_tokensOut)}";

    /// <summary>Token totals at turn start — TurnCompleted.Usage reconciles against these
    /// (Claude's per-message usage undercounts; result.usage is authoritative).</summary>
    public long TurnBaseIn { get; set; }
    public long TurnBaseOut { get; set; }

    private double _costUsd;
    public double CostUsd { get => _costUsd; set { if (Set(ref _costUsd, value)) OnChanged(nameof(CostLabel)); } }
    public string CostLabel => _costUsd > 0 ? "$" + _costUsd.ToString("0.0000") : "—";

    private string _draft = "";
    public string Draft { get => _draft; set { if (Set(ref _draft, value)) OnChanged(nameof(CanSend)); } }
    public bool CanSend => !string.IsNullOrWhiteSpace(_draft) && !IsRunning;

    private static string Fmt(long n) => n >= 1000 ? (n / 1000.0).ToString("0.0") + "k" : n.ToString();

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return span.ToString(@"h\:mm\:ss");
        return span.ToString(@"m\:ss");
    }

    private static string Ago(DateTime since)
    {
        var span = DateTime.Now - since;
        if (span.TotalMinutes >= 1) return FormatDuration(span);
        return Math.Max(0, (int)span.TotalSeconds) + "s";
    }
}
