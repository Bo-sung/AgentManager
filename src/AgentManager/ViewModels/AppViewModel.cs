using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using AgentManager.Core.Agents;
using AgentManager.Core.Events;
using AgentManager.Core.Session;
using AgentManager.Core.Translation;

namespace AgentManager.ViewModels;

public sealed class AppViewModel : ObservableObject
{
    private readonly OllamaTranslator _translator = new(new OllamaOptions());

    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public EngineDef[] Engines { get; } = Array.FindAll(EngineRegistry.All, e => e.Enabled);
    public string Project { get; } = "workspace";
    public string WorkingDirectory { get; set; }

    public AppViewModel()
    {
        WorkingDirectory = FindRepoRoot();
        NewAgentSelectedEngine = Engines[0];

        NewAgentCommand = new RelayCommand(_ => ShowNewAgent = true);
        CancelNewAgentCommand = new RelayCommand(_ => ShowNewAgent = false);
        CreateSessionCommand = new RelayCommand(_ => CreateSession(), _ => NewAgentSelectedEngine is not null);
        SelectSessionCommand = new RelayCommand(s => { if (s is SessionViewModel vm) ActiveSession = vm; });
        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => ActiveSession?.CanSend == true);
    }

    // ----- commands -----
    public RelayCommand NewAgentCommand { get; }
    public RelayCommand CancelNewAgentCommand { get; }
    public RelayCommand CreateSessionCommand { get; }
    public RelayCommand SelectSessionCommand { get; }
    public RelayCommand SendCommand { get; }

    // ----- active session -----
    private SessionViewModel? _active;
    public SessionViewModel? ActiveSession
    {
        get => _active;
        set { if (Set(ref _active, value)) OnChanged(nameof(HasActive)); }
    }
    public bool HasActive => _active is not null;

    // ----- new-agent overlay state -----
    private bool _showNew;
    public bool ShowNewAgent { get => _showNew; set => Set(ref _showNew, value); }
    private EngineDef? _newEngine;
    public EngineDef? NewAgentSelectedEngine { get => _newEngine; set => Set(ref _newEngine, value); }
    private string _newTitle = "";
    public string NewAgentTitle { get => _newTitle; set => Set(ref _newTitle, value); }

    // ----- translation + quota -----
    private bool _translationEnabled = true;
    public bool TranslationEnabled { get => _translationEnabled; set => Set(ref _translationEnabled, value); }
    private string _quotaText = "";
    public string QuotaText { get => _quotaText; set => Set(ref _quotaText, value); }

    // ----- counts -----
    public int RunningCount => CountBy("running");
    public int WaitingCount => CountBy("waiting");
    public int DoneCount => CountBy("done");
    private int CountBy(string s) { int n = 0; foreach (var x in Sessions) if (x.Status == s) n++; return n; }
    private void RefreshCounts() { OnChanged(nameof(RunningCount)); OnChanged(nameof(WaitingCount)); OnChanged(nameof(DoneCount)); }

    private void CreateSession()
    {
        var engine = NewAgentSelectedEngine ?? Engines[0];
        var title = string.IsNullOrWhiteSpace(NewAgentTitle) ? $"New {engine.Name} task" : NewAgentTitle.Trim();
        var branch = "agent/" + Slug(title);
        var s = new SessionViewModel("s" + DateTime.Now.Ticks, engine, title, branch, Project, engine.Models[0]);
        s.PropertyChanged += SessionStatusWatch;
        Sessions.Insert(0, s);
        ActiveSession = s;
        ShowNewAgent = false;
        var task = title;
        NewAgentTitle = "";
        RefreshCounts();
        _ = RunTurnAsync(s, task); // first turn = the task
    }

    private void SessionStatusWatch(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.Status)) RefreshCounts();
    }

    private async Task SendAsync()
    {
        var s = ActiveSession;
        if (s is null || !s.CanSend) return;
        var prompt = s.Draft.Trim();
        s.Draft = "";
        await RunTurnAsync(s, prompt);
    }

    /// <summary>Run one engine turn for a session and stream normalized events into its transcript.</summary>
    private async Task RunTurnAsync(SessionViewModel s, string prompt)
    {
        var dispatcher = Application.Current.Dispatcher;
        s.Transcript.Add(new UserBlock(prompt));
        s.Status = "running";
        s.Activity = "working…";

        var adapter = EngineRegistry.CreateAdapter(s.AgentId);
        var exe = EngineRegistry.ResolveExe(s.AgentId);
        if (adapter is null || exe is null)
        {
            s.Transcript.Add(new ErrorBlock("Engine unavailable", $"{s.AgentName} CLI를 찾을 수 없습니다."));
            s.Status = "error";
            return;
        }

        var tools = new Dictionary<string, ToolBlock>();
        var session = new AgentSession(adapter, exe, _translator, TranslationEnabled);
        session.EventReceived += ev => dispatcher.Invoke(() => Apply(s, ev, tools));

        var options = new SessionOptions { WorkingDirectory = WorkingDirectory, BypassPermissions = true };
        try
        {
            await Task.Run(() => session.RunAsync(options, prompt));
            if (s.Status == "running") s.Status = "done";
        }
        catch (Exception ex)
        {
            s.Transcript.Add(new ErrorBlock("Run failed", ex.Message));
            s.Status = "error";
        }
        s.Activity = s.Status == "done" ? "completed" : s.Activity;
    }

    private void Apply(SessionViewModel s, NormalizedEvent ev, Dictionary<string, ToolBlock> tools)
    {
        switch (ev)
        {
            case AssistantText at when !string.IsNullOrWhiteSpace(at.Text):
                s.Transcript.Add(new AgentTextBlock(at.Text));
                break;
            case ToolUseStarted u:
                var tb = new ToolBlock(u.ToolUseId, KindOf(u.Name), u.Name);
                tools[u.ToolUseId] = tb;
                s.Transcript.Add(tb);
                s.Activity = $"{u.Name}…";
                break;
            case ToolResult r:
                if (tools.TryGetValue(r.ToolUseId, out var t))
                {
                    t.Body = Trim(r.Content, 2000);
                    t.Stat = r.IsError ? "error" : "done";
                }
                else
                {
                    s.Transcript.Add(new ToolBlock(r.ToolUseId, "RUN", "result") { Body = Trim(r.Content, 2000), Stat = r.IsError ? "error" : "done" });
                }
                break;
            case TokenUsage k:
                s.TokensIn = k.InputTokens;
                s.TokensOut = k.OutputTokens;
                break;
            case QuotaUpdate q:
                QuotaText = $"QUOTA {q.Utilization:P0} · {q.RateLimitType}";
                break;
            case EngineError e when !e.Message.Contains("Reading additional input"):
                s.Transcript.Add(new ErrorBlock("stderr", e.Message));
                break;
            case TurnCompleted c:
                s.Status = c.IsError ? "error" : "done";
                s.Activity = "completed";
                break;
        }
    }

    private static string KindOf(string name) => name switch
    {
        "Read" or "Glob" or "Grep" or "LS" => "READ",
        "Edit" or "MultiEdit" or "Write" => "EDIT",
        _ => "RUN",
    };

    private static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    private static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-')[..Math.Min(28, slug.Trim('-').Length)].TrimEnd('-');
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")))
                return d.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
