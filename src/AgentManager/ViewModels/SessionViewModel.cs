using System.Collections.ObjectModel;
using AgentManager.Core.Agents;

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

    private string _renameDraft = "";
    /// <summary>Inline rename 입력(컨텍스트 메뉴) — 프로젝트의 RenameDraft와 동일 패턴. 미영속.</summary>
    public string RenameDraft { get => _renameDraft; set => Set(ref _renameDraft, value); }

    /// <summary>The active choice — heuristic A/B/C detected in the last assistant message, or a
    /// structured question pushed by the ask-user skill (single/multi-select, one or more pages).
    /// Null = no choice (composer shown). Set on turn completion / ask ingest, cleared on a new turn,
    /// dismiss, or after the flow is answered. Not persisted.</summary>
    private ChoiceFlow? _choice;
    public ChoiceFlow? ActiveChoice { get => _choice; set { if (Set(ref _choice, value)) OnChanged(nameof(HasChoice)); } }
    public bool HasChoice => _choice is not null;

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

    /// <summary>True면 worktree 격리를 건너뛰고 프로젝트 루트에서 작업(EnsureWorktreeAsync가 생성 생략).
    /// New Agent 모달의 "워크트리 미사용"을 켜면 설정됨. 영속됨.</summary>
    private bool _worktreeOptOut;
    public bool WorktreeOptOut { get => _worktreeOptOut; set { if (Set(ref _worktreeOptOut, value)) { OnChanged(nameof(BranchDisplay)); OnChanged(nameof(WorktreePill)); OnChanged(nameof(WorktreeLabel)); } } }

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
        _reasoningEffort = RecommendedEffort(engine.Id, model);
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
    /// <summary>Antigravity 터미널 전용 액션(외부 터미널 실행) 노출 게이트. ConPTY 캡처 트랜스크립트 대신
    /// 진짜 콘솔에서 agy의 풀 TUI/인증을 쓰기 위한 escape hatch 버튼 가시성에 바인딩.</summary>
    public bool IsAgy => AgentId == "agy";

    /// <summary>추론 강도 옵션 — 엔진별 공식 단계(전부 CLI/API로 검증). agy만 추론 플래그 없음 → 피커 비노출.
    /// claude(--effort): low/medium/high/xhigh/max ("claude --effort zzz" 경고의 Valid values).
    /// codex(model_reasoning_effort): none/minimal/low/medium/high/xhigh (codex API invalid_enum_value).
    /// pi(--thinking): off/minimal/low/medium/high/xhigh (pi --help · PHASE0_PI_RPC).
    /// "default"는 UI 센티넬(플래그 생략 → 엔진 기본 사용); 어댑터가 "default"·빈값이면 플래그 미전달.</summary>
    public bool HasEffort => AgentId is not "agy";
    public string[] EffortOptions => AgentId switch
    {
        "gx" => ["none", "minimal", "low", "medium", "high", "xhigh"],
        "pi" => ["default", "off", "minimal", "low", "medium", "high", "xhigh"],
        _    => ["default", "low", "medium", "high", "xhigh", "max"], // cc
    };
    private string _reasoningEffort = "";
    public string ReasoningEffort { get => _reasoningEffort; set => Set(ref _reasoningEffort, value); }

    /// <summary>Recommended DEFAULT reasoning effort for an engine+model — a smart default, NOT a gate
    /// (the user can still pick any level). cc recommends medium for Opus (per cc's own guidance: "We
    /// recommend medium effort for Opus"); other cc models use cc's default; gx defaults to medium.</summary>
    public static string RecommendedEffort(string? engineId, string? model) => engineId switch
    {
        "cc" when model is { } m && m.Contains("opus", StringComparison.OrdinalIgnoreCase) => "medium",
        "gx" => "medium",
        "cc" or "pi" => "default",
        _ => "",
    };

    /// <summary>The worktree's live current branch, read from git after each review refresh — the agent
    /// may switch/create a branch, and the creation-time <see cref="Branch"/> would then be stale. Null
    /// until first read; displays fall back to <see cref="Branch"/>.</summary>
    private string? _currentBranch;
    public string? CurrentBranch
    {
        get => _currentBranch;
        set { if (Set(ref _currentBranch, value)) { OnChanged(nameof(EffectiveBranch)); OnChanged(nameof(BranchTail)); OnChanged(nameof(BranchDisplay)); OnChanged(nameof(WorktreePill)); } }
    }

    /// <summary>The branch to show: the live git branch when known, else the creation branch.</summary>
    public string EffectiveBranch => string.IsNullOrEmpty(_currentBranch) ? Branch : _currentBranch!;

    /// <summary>Last segment of the branch, e.g. "agent/foo-bar" → "foo-bar" (composer pill).</summary>
    public string BranchTail => EffectiveBranch.Contains('/') ? EffectiveBranch[(EffectiveBranch.LastIndexOf('/') + 1)..] : EffectiveBranch;

    /// <summary>Branch label for list/header pills. When the user opted out of a worktree, no branch is
    /// ever created (the agent works in the project's main tree) — so showing the phantom "agent/…"
    /// name is misleading; show a "shared tree" marker instead.</summary>
    public string BranchDisplay => WorktreeOptOut ? AgentManager.App.L("L.BranchShared") : EffectiveBranch;

    /// <summary>Composer worktree pill: "Worktree · &lt;tail&gt;" when isolated, "main tree (shared)"
    /// when opted out (no worktree is created).</summary>
    public string WorktreePill => WorktreeOptOut ? AgentManager.App.L("L.SharedTreePill") : AgentManager.App.L("L.WorktreePrefix") + BranchTail;

    /// <summary>Archived sessions are hidden from the Active/Project groups (kept in storage).</summary>
    private bool _isArchived;
    public bool IsArchived { get => _isArchived; set => Set(ref _isArchived, value); }

    /// <summary>Per-session sandbox: ReadOnly(분석만)/WorkspaceWrite/DangerFullAccess.</summary>
    private Core.Agents.SandboxMode _sandbox = Core.Agents.SandboxMode.DangerFullAccess;
    public Core.Agents.SandboxMode Sandbox
    {
        get => _sandbox;
        set { if (Set(ref _sandbox, value)) { OnChanged(nameof(PermissionMode)); OnChanged(nameof(PermissionRisk)); OnChanged(nameof(CurrentPermissionModeItem)); } }
    }

    /// <summary>승인 broker Stage 1 (Claude만): true면 툴 실행 전 사용자 승인 요구.</summary>
    private bool _requireApproval;
    public bool RequireApproval
    {
        get => _requireApproval;
        set { if (Set(ref _requireApproval, value)) { OnChanged(nameof(PermissionMode)); OnChanged(nameof(PermissionRisk)); OnChanged(nameof(CurrentPermissionModeItem)); } }
    }

    // ----- 권한/안전 모드 (engine-aware) -------------------------------------------------
    // 엔진별 네이티브 모드를 (Sandbox, RequireApproval)의 친화적 뷰로 노출 — 백킹 필드는 그대로라
    // 런 경로/어댑터는 불변. 매핑은 CLI 실측 기준(ClaudeAdapter: ReadOnly→plan / broker / skip;
    // CodexAdapter: bypass(=RequireApproval false) / --sandbox read-only|workspace-write).
    // agy는 항상 skip-permissions(고정 full), pi는 권한 개념 없음(Permissions:false) → 선택지 없음.

    /// <summary>cc/gx만 모드 선택 가능. agy/pi는 고정(피커 비노출, 정적 배지).</summary>
    public bool HasPermissionModeChoice => AgentId is "cc" or "gx";

    /// <summary>엔진별 네이티브 모드 목록(메타 포함, 왼→오 = 안전→위험). 컴포저 칩/드롭다운이 바인딩.</summary>
    public PermissionModeOption[] PermissionModeItems => AgentId switch
    {
        "cc" => PermissionModes.Cc,
        "gx" => PermissionModes.Gx,
        "agy" => PermissionModes.Agy,
        _ => PermissionModes.Pi,
    };

    /// <summary>현재 선택된 모드의 메타(칩 표시용).</summary>
    public PermissionModeOption CurrentPermissionModeItem =>
        System.Array.Find(PermissionModeItems, m => m.Id == PermissionMode) ?? PermissionModeItems[0];

    /// <summary>agy/pi 잠금 사유(툴팁).</summary>
    public string PermissionLockReason => AgentId switch
    {
        "agy" => "Antigravity는 항상 전체 권한으로 동작합니다. 모드를 변경할 수 없습니다.",
        "pi" => "Pi에는 권한 모드 개념이 없습니다. 변경할 항목이 없습니다.",
        _ => "",
    };

    /// <summary>현재 모드 — (Sandbox, RequireApproval)에서 파생, set 시 둘을 엔진별로 설정.</summary>
    public string PermissionMode
    {
        get => AgentId switch
        {
            "cc" => Sandbox == Core.Agents.SandboxMode.ReadOnly ? "plan" : (RequireApproval ? "default" : "bypass"),
            "gx" => !RequireApproval ? "full-access"
                  : Sandbox == Core.Agents.SandboxMode.ReadOnly ? "read-only" : "workspace-write",
            "agy" => "full",
            _ => "default", // pi (권한 없음)
        };
        set
        {
            switch (AgentId)
            {
                case "cc":
                    if (value == "plan") { Sandbox = Core.Agents.SandboxMode.ReadOnly; RequireApproval = true; }
                    else if (value == "bypass") { Sandbox = Core.Agents.SandboxMode.WorkspaceWrite; RequireApproval = false; }
                    else { Sandbox = Core.Agents.SandboxMode.WorkspaceWrite; RequireApproval = true; } // default(ask)
                    break;
                case "gx":
                    if (value == "read-only") { Sandbox = Core.Agents.SandboxMode.ReadOnly; RequireApproval = true; }
                    else if (value == "full-access") { Sandbox = Core.Agents.SandboxMode.DangerFullAccess; RequireApproval = false; }
                    else { Sandbox = Core.Agents.SandboxMode.WorkspaceWrite; RequireApproval = true; } // workspace-write
                    break;
                // agy/pi: 고정 — 변경 없음
            }
        }
    }

    /// <summary>공통 위험 그라데이션 색 토큰(r0/r1/r3/rn) — 현재 모드 메타에서. RiskBrush 컨버터가 색으로.</summary>
    public string PermissionRisk => CurrentPermissionModeItem.Risk;

    /// <summary>컴포저 권한 모드 드롭다운 항목 메타. Risk = r0(안전)/r1(쓰기)/r2(완화)/r3(전체)/rn(없음).</summary>
    public sealed record PermissionModeOption(string Id, string Name, string Desc, string Flag, string Risk);

    private static class PermissionModes
    {
        public static readonly PermissionModeOption[] Cc =
        [
            new("plan",    "Plan",          "읽기·계획만. 파일 변경이나 명령 실행을 하지 않음.", "--permission-mode plan", "r0"),
            new("default", "Default (ask)", "쓰기/실행 전 매번 사용자에게 승인을 요청 (broker).", "broker · ask", "r1"),
            new("bypass",  "Bypass",        "모든 권한 확인을 건너뜀. 승인 없이 파일·명령 실행.", "--dangerously-skip-permissions", "r3"),
        ];
        public static readonly PermissionModeOption[] Gx =
        [
            new("read-only",       "Read-only",       "파일 읽기만 허용. 쓰기·네트워크·명령 차단.", "--sandbox read-only", "r0"),
            new("workspace-write", "Workspace-write", "워크스페이스 내 파일 쓰기 허용. 외부는 차단.", "--sandbox workspace-write", "r1"),
            new("full-access",     "Full access",     "파일·네트워크·명령 전체 허용. 샌드박스 없음.", "--sandbox danger-full-access", "r3"),
        ];
        public static readonly PermissionModeOption[] Agy =
        [
            new("full", "Full (auto)", "항상 전체 권한으로 자동 실행. 변경 불가.", "always skip permissions", "r3"),
        ];
        public static readonly PermissionModeOption[] Pi =
        [
            new("default", "Default", "권한 개념이 없는 엔진. 모드 선택 항목이 없음.", "no permission surface", "rn"),
        ];
    }

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
