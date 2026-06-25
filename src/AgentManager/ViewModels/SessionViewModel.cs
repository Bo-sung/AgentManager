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
    public ObservableCollection<ArtifactViewModel> Artifacts { get; } = [];
    public ObservableCollection<NativeWorkItemViewModel> NativeWorkItems { get; } = [];

    /// <summary>Files queued for the next turn (paste/picker): images → base64 blocks, docs →
    /// inlined prompt text. Cleared on send; not persisted.</summary>
    public ObservableCollection<PendingAttachment> PendingAttachments { get; } = [];

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
        AvailableModels = engine.Models;
        _reasoningEffort = engine.Id switch { "gx" => "medium", "cc" => "default", _ => "" };
        StartedAt = startedAt ?? DateTime.Now;
        NativeWorkItems.CollectionChanged += (_, _) => OnChanged(nameof(HasNativeWorkItems));
    }

    public bool HasNativeWorkItems => NativeWorkItems.Count > 0;

    private string _title;
    public string Title { get => _title; set => Set(ref _title, value); }

    /// <summary>Models offered by this session's engine (for the composer model menu).</summary>
    public string[] AvailableModels { get; private set; } = [];

    /// <summary>엔진별 컴포저 분기: 코덱스만 추론 강도 선택 노출 + 보라 테두리.</summary>
    public bool IsCodex => AgentId == "gx";
    public bool IsClaude => AgentId == "cc";

    /// <summary>추론 강도 옵션 — 엔진별 가짓수가 다름 (실측: claude --effort 5단계 + default, codex 4단계,
    /// antigravity는 플래그 없음 → 피커 비노출).</summary>
    public bool HasEffort => AgentId is not "agy";
    public string[] EffortOptions => IsCodex
        ? ["low", "medium", "high", "xhigh"]
        : ["default", "low", "medium", "high", "xhigh", "max"];
    private string _reasoningEffort = "";
    public string ReasoningEffort { get => _reasoningEffort; set => Set(ref _reasoningEffort, value); }

    /// <summary>Last segment of the branch, e.g. "agent/foo-bar" → "foo-bar" (composer pill).</summary>
    public string BranchTail => Branch.Contains('/') ? Branch[(Branch.LastIndexOf('/') + 1)..] : Branch;

    /// <summary>Archived sessions are hidden from the Active/Project groups (kept in storage).</summary>
    private bool _isArchived;
    public bool IsArchived { get => _isArchived; set => Set(ref _isArchived, value); }

    /// <summary>Per-session sandbox: ReadOnly(분석만)/WorkspaceWrite/DangerFullAccess.</summary>
    private Core.Agents.SandboxMode _sandbox = Core.Agents.SandboxMode.DangerFullAccess;
    public Core.Agents.SandboxMode Sandbox { get => _sandbox; set => Set(ref _sandbox, value); }

    /// <summary>승인 broker Stage 1 (Claude만): true면 툴 실행 전 사용자 승인 요구.</summary>
    private bool _requireApproval;
    public bool RequireApproval { get => _requireApproval; set => Set(ref _requireApproval, value); }

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
                OnChanged(nameof(IsBusy));
                OnChanged(nameof(CanSend));
                RefreshRuntimeLabels();
            }
        }
    }
    public string StatusLabel => _status switch
    {
        "running" => AgentManager.App.L("L.StatusRunning"),
        "waiting" => AgentManager.App.L("L.StatusAwaitingInput"),
        "done" => AgentManager.App.L("L.StatusCompleted"),
        "error" => AgentManager.App.L("L.StatusFailed"),
        _ => AgentManager.App.L("L.StatusIdle")
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
    public string TranslationLabel => TranslationEnabled ? AgentManager.App.L("L.TranslationOn") : AgentManager.App.L("L.TranslationOff");

    // ----- 워커 위임 (Worker Delegation) -----
    /// <summary>세션 역할. Plain(기본)/Main/Worker.</summary>
    private AgentManager.Core.Workers.SessionRole _role = AgentManager.Core.Workers.SessionRole.Plain;
    public AgentManager.Core.Workers.SessionRole Role
    {
        get => _role;
        set { if (Set(ref _role, value)) { OnChanged(nameof(IsWorker)); OnChanged(nameof(IsMain)); } }
    }
    public bool IsWorker => _role == AgentManager.Core.Workers.SessionRole.Worker;
    public bool IsMain => _role == AgentManager.Core.Workers.SessionRole.Main;

    /// <summary>워커 고정 행동 규칙(위임 프롬프트 앞에 부착). 워커일 때만 의미.</summary>
    private string _behaviorPreamble = "";
    public string BehaviorPreamble { get => _behaviorPreamble; set => Set(ref _behaviorPreamble, value); }

    /// <summary>워커 고정 번역 언어쌍(생성 시 고정). null = 전역 설정 사용(일반 세션).</summary>
    private string? _translateSourceLanguage;
    public string? TranslateSourceLanguage { get => _translateSourceLanguage; set => Set(ref _translateSourceLanguage, value); }
    private string? _translateTargetLanguage;
    public string? TranslateTargetLanguage { get => _translateTargetLanguage; set => Set(ref _translateTargetLanguage, value); }

    /// <summary>워커의 현재/마지막 담당 메인 세션 id(사이드바 "담당 메인" 라벨용).</summary>
    private string? _lastMainSessionId;
    public string? LastMainSessionId { get => _lastMainSessionId; set => Set(ref _lastMainSessionId, value); }

    /// <summary>워커 위임 UI의 번역 정책 칩.</summary>
    public string DelegationTrLabel => TranslationEnabled
        ? $"TR · {_translateSourceLanguage ?? "Korean"}→{_translateTargetLanguage ?? "English"}"
        : "TR OFF";
    /// <summary>위임 목록에서 사용 중(busy) 표시 — 실행 상태 기준.</summary>
    public bool IsBusy => _status == "running";

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
            var activity = string.IsNullOrWhiteSpace(Activity) ? AgentManager.App.L("L.WaitingEngineOutput") : Activity;
            return IsQuiet ? AgentManager.App.L("L.NoOutputFor", activity, Ago(_lastSignalAt ?? _runStartedAt ?? DateTime.Now)) : activity;
        }
    }

    public string RunningElapsedLabel => IsRunning && _runStartedAt is { } started ? FormatDuration(DateTime.Now - started) : "";
    public string LastSignalLabel => IsRunning && _lastSignalAt is { } last ? AgentManager.App.L("L.LastSignalAgo", Ago(last)) : AgentManager.App.L("L.WaitingFirstSignal");
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

    private string _diffText = AgentManager.App.L("L.SelectDiffPrompt");
    public string DiffText { get => _diffText; set => Set(ref _diffText, value); }

    private string _reviewStatus = AgentManager.App.L("L.NoIsolatedWorktreeYet");
    public string ReviewStatus { get => _reviewStatus; set => Set(ref _reviewStatus, value); }

    public string WorktreeLabel => Isolated && WorktreePath is not null ? WorktreePath : AgentManager.App.L("L.NotIsolated");

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
    /// <summary>Claude는 result.usage의 실비용(USD). Codex/Gemini는 구독(플랜) 정산이라 비용 미보고 — "plan"으로 명시.</summary>
    public string CostLabel => _costUsd > 0 ? "$" + _costUsd.ToString("0.0000")
        : AgentId != "cc" && TokensIn + TokensOut > 0 ? AgentManager.App.L("L.Plan") : "—";

    private string _draft = "";
    public string Draft { get => _draft; set { if (Set(ref _draft, value)) OnChanged(nameof(CanSend)); } }
    public bool CanSend => !string.IsNullOrWhiteSpace(_draft) && !IsRunning;

    private int _diffAdded;
    public int DiffAdded
    {
        get => _diffAdded;
        internal set
        {
            if (Set(ref _diffAdded, value))
            {
                OnChanged(nameof(HasDiff));
                OnChanged(nameof(DiffAddedStar));
                OnChanged(nameof(DiffRemainderStar));
            }
        }
    }

    private int _diffRemoved;
    public int DiffRemoved
    {
        get => _diffRemoved;
        internal set
        {
            if (Set(ref _diffRemoved, value))
            {
                OnChanged(nameof(HasDiff));
                OnChanged(nameof(DiffRemovedStar));
                OnChanged(nameof(DiffRemainderStar));
            }
        }
    }

    private int _diffFiles;
    public int DiffFiles
    {
        get => _diffFiles;
        internal set
        {
            if (Set(ref _diffFiles, value))
            {
                OnChanged(nameof(HasDiff));
                OnChanged(nameof(FilesSuffix));
            }
        }
    }

    public bool HasDiff => _diffFiles > 0;

    public int DiffRemainder => (DiffAdded + DiffRemoved) == 0 ? 1 : 0;

    public System.Windows.GridLength DiffAddedStar => new System.Windows.GridLength(DiffAdded, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DiffRemovedStar => new System.Windows.GridLength(DiffRemoved, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DiffRemainderStar => new System.Windows.GridLength(DiffRemainder, System.Windows.GridUnitType.Star);

    public string FilesSuffix
    {
        get
        {
            var key = DiffFiles == 1 ? "L.OrchDiffFileSuffixOne" : "L.OrchDiffFileSuffixMany";
            return AgentManager.App.L(key);
        }
    }

    private static string Fmt(long n) => n >= 1000 ? (n / 1000.0).ToString("0.0") + "k" : n.ToString();
    public string StartedAtLabel => StartedAt.ToString("yyyy-MM-dd HH:mm");

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
