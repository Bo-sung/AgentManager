using System.Collections.ObjectModel;
using AgentManager.Core.Workers;

namespace AgentManager.ViewModels;

/// <summary>워커 위임 UI 상태 + 커맨드(모달/카드/수신함). 로직은 AppViewModel.Delegation.cs.</summary>
public sealed partial class AppViewModel
{
    // ----- 모달 가시성 -----
    private bool _showWorkerAssign;
    public bool ShowWorkerAssign { get => _showWorkerAssign; set => Set(ref _showWorkerAssign, value); }
    private bool _showNoIdleWorker;
    public bool ShowNoIdleWorker { get => _showNoIdleWorker; set { if (Set(ref _showNoIdleWorker, value) && value) OnChanged(nameof(BusyWorkers)); } }
    /// <summary>The currently-busy workers — shown in the "no idle worker" modal so the user can see
    /// what's blocking. Snapshotted when the modal opens (re-notified by the setter above).</summary>
    public IEnumerable<SessionViewModel> BusyWorkers => WorkerPool.Where(IsWorkerBusy);

    // ----- 위임 에디터 상태 -----
    private SessionViewModel? _delegateMain;
    private string _delegatePrompt = "";
    public string DelegatePrompt { get => _delegatePrompt; set { if (Set(ref _delegatePrompt, value)) { OnChanged(nameof(CanConfirmDelegate)); OnChanged(nameof(CanDelegateAll)); } } }
    private bool _delegateWorktreeShared;
    public bool DelegateWorktreeShared { get => _delegateWorktreeShared; set => Set(ref _delegateWorktreeShared, value); }
    private bool _delegateAutoInject = true;
    public bool DelegateAutoInject { get => _delegateAutoInject; set => Set(ref _delegateAutoInject, value); }
    private bool _delegateNewWorkerOpen;
    public bool DelegateNewWorkerOpen { get => _delegateNewWorkerOpen; set { if (Set(ref _delegateNewWorkerOpen, value)) OnChanged(nameof(CanConfirmDelegate)); } }
    private SessionViewModel? _selectedWorker;
    public SessionViewModel? SelectedWorker
    {
        get => _selectedWorker;
        set
        {
            if (Set(ref _selectedWorker, value))
            {
                if (value is not null && _delegateNewWorkerOpen) { _delegateNewWorkerOpen = false; OnChanged(nameof(DelegateNewWorkerOpen)); }
                OnChanged(nameof(CanConfirmDelegate));
            }
        }
    }

    // ----- 새 워커 드래프트 -----
    private EngineDef _newWorkerEngine = EngineRegistry.All[0];
    public EngineDef NewWorkerEngine
    {
        get => _newWorkerEngine;
        set { if (Set(ref _newWorkerEngine, value)) { OnChanged(nameof(NewWorkerModels)); NewWorkerModel = DefaultModelFor(value.Id) is { Length: > 0 } dm ? dm : value.Models[0]; } }
    }
    public string[] NewWorkerModels => _newWorkerEngine.Models;
    private string _newWorkerModel = EngineRegistry.All[0].Models[0];
    public string NewWorkerModel { get => _newWorkerModel; set => Set(ref _newWorkerModel, value); }
    private string _newWorkerName = "";
    public string NewWorkerName { get => _newWorkerName; set { if (Set(ref _newWorkerName, value)) OnChanged(nameof(CanConfirmDelegate)); } }
    private bool _newWorkerTranslationEnabled = true;
    public bool NewWorkerTranslationEnabled { get => _newWorkerTranslationEnabled; set => Set(ref _newWorkerTranslationEnabled, value); }
    private string _newWorkerSource = "Korean";
    public string NewWorkerSource { get => _newWorkerSource; set => Set(ref _newWorkerSource, value); }
    private string _newWorkerTarget = "English";
    public string NewWorkerTarget { get => _newWorkerTarget; set => Set(ref _newWorkerTarget, value); }

    /// <summary>워커 선택 엔진 옵션(New Agent와 동일 집합).</summary>
    public EngineDef[] WorkerEngines => EngineRegistry.All;

    /// <summary>위임 대상 풀(활성 프로젝트의 워커).</summary>
    public ObservableCollection<SessionViewModel> WorkerPool { get; } = [];

    /// <summary>위임 버튼 활성 조건: 프롬프트 + (기존 워커 선택 | 새 워커 이름).</summary>
    public bool CanConfirmDelegate =>
        !string.IsNullOrWhiteSpace(_delegatePrompt)
        && (_delegateNewWorkerOpen ? !string.IsNullOrWhiteSpace(_newWorkerName) : _selectedWorker is not null);

    /// <summary>유휴(비실행) 워커 수 — 일괄 위임 버튼 라벨/활성.</summary>
    public int IdleWorkerCount => WorkerPool.Count(w => !IsWorkerBusy(w));
    /// <summary>일괄 fan-out 가능: 프롬프트 + 유휴 워커 ≥ 2.</summary>
    public bool CanDelegateAll => !string.IsNullOrWhiteSpace(_delegatePrompt) && IdleWorkerCount >= 2;

    // ----- 수신함(활성 메인) -----
    public IEnumerable<WorkerDelegationViewModel> ReadyReportsActive =>
        Delegations.Where(d => d.MainSessionId == ActiveSession?.Id && d.State == DelegationState.Ready);
    public int ReadyReportsActiveCount => ReadyReportsActive.Count();
    public bool HasReadyReportsActive => ReadyReportsActiveCount > 0;
    public bool CanMergeReports => ReadyReportsActiveCount >= 2;

    /// <summary>수신함/뱃지 갱신 통지.</summary>
    public void NotifyInbox()
    {
        OnChanged(nameof(ReadyReportsActive));
        OnChanged(nameof(ReadyReportsActiveCount));
        OnChanged(nameof(HasReadyReportsActive));
        OnChanged(nameof(CanMergeReports));
    }

    public void RefreshWorkerPool()
    {
        WorkerPool.Clear();
        foreach (var w in _allSessions.Where(s => s.IsWorker && (ActiveProject is null || s.ProjectId == ActiveProject.Id)))
            WorkerPool.Add(w);
        OnChanged(nameof(IdleWorkerCount));
        OnChanged(nameof(CanDelegateAll));
    }

    // ----- 커맨드 -----
    public RelayCommand OpenDelegateCommand { get; private set; } = null!;
    public RelayCommand SelectWorkerEngineCommand { get; private set; } = null!;
    public RelayCommand PickWorkerCommand { get; private set; } = null!;
    public RelayCommand ToggleNewWorkerCommand { get; private set; } = null!;
    public RelayCommand ConfirmDelegateCommand { get; private set; } = null!;
    public RelayCommand DelegateAllCommand { get; private set; } = null!;
    public RelayCommand CancelDelegateCommand { get; private set; } = null!;
    public RelayCommand NoIdleCreateCommand { get; private set; } = null!;
    public RelayCommand PasteReportCommand { get; private set; } = null!;
    public RelayCommand RedelegateCommand { get; private set; } = null!;
    public RelayCommand OpenWorkerCommand { get; private set; } = null!;
    public RelayCommand MergeReportsCommand { get; private set; } = null!;

    private void InitDelegationCommands()
    {
        OpenDelegateCommand = new RelayCommand(p => OpenDelegate(p as string ?? (p as AgentTextBlock)?.DisplayText ?? ""));
        SelectWorkerEngineCommand = new RelayCommand(p => { if (p is EngineDef def) NewWorkerEngine = def; });
        PickWorkerCommand = new RelayCommand(p => { if (p is SessionViewModel w && !IsWorkerBusy(w)) { SelectedWorker = w; DelegateNewWorkerOpen = false; } });
        ToggleNewWorkerCommand = new RelayCommand(_ => { DelegateNewWorkerOpen = !DelegateNewWorkerOpen; if (DelegateNewWorkerOpen) SelectedWorker = null; });
        ConfirmDelegateCommand = new RelayCommand(_ => ConfirmDelegate(), _ => CanConfirmDelegate);
        DelegateAllCommand = new RelayCommand(_ => DelegateAll(), _ => CanDelegateAll);
        CancelDelegateCommand = new RelayCommand(_ => { ShowWorkerAssign = false; ShowNoIdleWorker = false; });
        NoIdleCreateCommand = new RelayCommand(_ => { ShowNoIdleWorker = false; DelegateNewWorkerOpen = true; SelectedWorker = null; ShowWorkerAssign = true; });
        PasteReportCommand = new RelayCommand(p => { if (p is WorkerDelegationViewModel d) PasteReport(d); });
        RedelegateCommand = new RelayCommand(p => { if (p is WorkerDelegationViewModel d) Redelegate(d); });
        OpenWorkerCommand = new RelayCommand(p => { if (p is WorkerDelegationViewModel d) OpenWorker(d); });
        MergeReportsCommand = new RelayCommand(_ => { var m = ActiveSession; if (m is not null) { InjectMergedReports(m); NotifyInbox(); } });
    }

    private void ResetWorkerDraft()
    {
        NewWorkerEngine = EngineRegistry.All[0];
        NewWorkerName = "";
        NewWorkerTranslationEnabled = true;
        NewWorkerSource = "Korean";
        NewWorkerTarget = "English";
    }

    private void OpenDelegate(string sourceText)
    {
        var main = ActiveSession;
        if (main is null) return;
        if (main.Role == SessionRole.Plain) main.Role = SessionRole.Main; // 위임을 보내면 메인 역할
        _delegateMain = main;
        DelegatePrompt = sourceText ?? "";
        DelegateWorktreeShared = false;
        DelegateAutoInject = true;
        ResetWorkerDraft();
        RefreshWorkerPool();
        var idle = WorkerPool.Where(w => !IsWorkerBusy(w)).ToList();
        if (WorkerPool.Count > 0 && idle.Count == 0)
        {
            ShowNoIdleWorker = true; // 전부 사용 중
            return;
        }
        DelegateNewWorkerOpen = idle.Count == 0;       // 워커 없으면 새 워커 폼
        SelectedWorker = idle.FirstOrDefault();
        ShowWorkerAssign = true;
    }

    private void ConfirmDelegate()
    {
        if (!CanConfirmDelegate || _delegateMain is null) return;
        SessionViewModel? worker;
        if (DelegateNewWorkerOpen)
        {
            if (ActiveProject is not { } project) return;
            worker = CreateWorkerSession(NewWorkerEngine, NewWorkerModel, project, NewWorkerName,
                NewWorkerTranslationEnabled, NewWorkerSource, NewWorkerTarget, WorkerBehaviorPreamble);
        }
        else worker = SelectedWorker;
        if (worker is null) return;
        if (IsWorkerBusy(worker)) { ShowWorkerAssign = false; ShowNoIdleWorker = true; return; }

        var main = _delegateMain;
        var prompt = _delegatePrompt;
        var shared = _delegateWorktreeShared;
        var auto = _delegateAutoInject;
        ShowWorkerAssign = false;
        _ = RunDelegationAsync(main, worker, prompt, shared, auto);
    }

    /// <summary>일괄 fan-out: 같은 프롬프트를 모든 유휴 워커에 동시에 위임(워커 cap 내 병렬).</summary>
    private void DelegateAll()
    {
        if (!CanDelegateAll || _delegateMain is null) return;
        var main = _delegateMain;
        var prompt = _delegatePrompt;
        var shared = _delegateWorktreeShared;
        var idle = WorkerPool.Where(w => !IsWorkerBusy(w)).ToList();
        ShowWorkerAssign = false;
        // fan-out collects every report in the inbox for the user to merge-inject — no per-report
        // auto-inject (that would interleave N reports into the draft one by one).
        foreach (var w in idle)
            _ = RunDelegationAsync(main, w, prompt, shared, auto: false);
    }

    private async Task RunDelegationAsync(SessionViewModel main, SessionViewModel worker, string prompt, bool shared, bool auto)
    {
        var d = await DelegateAsync(main, worker, prompt, shared);
        NotifyInbox();
        // 자동 복귀: 메인이 실행 중이 아니면 즉시 주입(단건). 다수는 사용자가 수신함에서 합쳐 주입.
        if (auto && d is { State: DelegationState.Ready } && !_running.ContainsKey(main.Id))
        {
            InjectReport(main, d);
            NotifyInbox();
        }
    }

    private void PasteReport(WorkerDelegationViewModel d)
    {
        var main = _allSessions.FirstOrDefault(s => s.Id == d.MainSessionId);
        if (main is null) return;
        InjectReport(main, d);
        NotifyInbox();
    }

    private void Redelegate(WorkerDelegationViewModel d)
    {
        var main = _allSessions.FirstOrDefault(s => s.Id == d.MainSessionId) ?? ActiveSession;
        if (main is null) return;
        _delegateMain = main;
        DelegatePrompt = d.Prompt;
        DelegateWorktreeShared = d.SharedWorktree;
        RefreshWorkerPool();
        SelectedWorker = WorkerPool.FirstOrDefault(w => w.Id == d.WorkerSessionId);
        DelegateNewWorkerOpen = SelectedWorker is null;
        ShowWorkerAssign = true;
    }

    private void OpenWorker(WorkerDelegationViewModel d)
    {
        var worker = _allSessions.FirstOrDefault(s => s.Id == d.WorkerSessionId);
        if (worker is null) return;
        ActiveSession = worker;
        CurrentView = MainViewKind.Session;
    }
}
