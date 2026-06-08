using System.Collections.ObjectModel;

namespace AgentManager.ViewModels;

public sealed class SessionViewModel : ObservableObject
{
    public string Id { get; }
    public string AgentId { get; }
    public string Badge { get; }
    public string AgentName { get; }
    public string Cli { get; }
    public string Title { get; }
    public string Branch { get; }
    public string Project { get; }
    public DateTime StartedAt { get; } = DateTime.Now;
    public ObservableCollection<TranscriptItem> Transcript { get; } = [];

    /// <summary>Per-session git worktree (isolation). Null = ran directly (non-git folder).</summary>
    public string? WorktreePath { get; set; }
    public bool Isolated { get; set; }
    public bool WorktreeAttempted { get; set; }

    public SessionViewModel(string id, EngineDef engine, string title, string branch, string project, string model)
    {
        Id = id; AgentId = engine.Id; Badge = engine.Badge; AgentName = engine.Name; Cli = engine.Cli;
        Title = title; Branch = branch; Project = project; _model = model;
    }

    private string _status = "idle";
    public string Status
    {
        get => _status;
        set { if (Set(ref _status, value)) { OnChanged(nameof(StatusLabel)); OnChanged(nameof(IsRunning)); OnChanged(nameof(IsLive)); } }
    }
    public string StatusLabel => _status switch
    {
        "running" => "Running", "waiting" => "Awaiting input", "done" => "Completed", "error" => "Failed", _ => "Idle"
    };
    public bool IsRunning => _status == "running";
    public bool IsLive => _status is "running" or "waiting";

    private string _model;
    public string Model { get => _model; set => Set(ref _model, value); }

    private string _activity = "";
    public string Activity { get => _activity; set => Set(ref _activity, value); }

    private long _tokensIn, _tokensOut;
    public long TokensIn { get => _tokensIn; set { if (Set(ref _tokensIn, value)) OnChanged(nameof(TokensLabel)); } }
    public long TokensOut { get => _tokensOut; set { if (Set(ref _tokensOut, value)) OnChanged(nameof(TokensLabel)); } }
    public string TokensLabel => $"{Fmt(_tokensIn)} / {Fmt(_tokensOut)}";

    private string _draft = "";
    public string Draft { get => _draft; set { if (Set(ref _draft, value)) OnChanged(nameof(CanSend)); } }
    public bool CanSend => !string.IsNullOrWhiteSpace(_draft) && !IsRunning;

    private static string Fmt(long n) => n >= 1000 ? (n / 1000.0).ToString("0.0") + "k" : n.ToString();
}
