using System.Collections.ObjectModel;
using AgentManager.Core.Workers;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    /// <summary>메인↔워커 위임 이력(메인 트랜스크립트 카드 + "보고 수신함" 바인딩).</summary>
    public ObservableCollection<WorkerDelegationViewModel> Delegations { get; } = [];

    /// <summary>워커가 현재 실행 중인지(busy). UI는 busy면 "유휴 워커 없음" 흐름.</summary>
    public bool IsWorkerBusy(SessionViewModel worker) => worker is not null && _running.ContainsKey(worker.Id);

    /// <summary>지정 메인의 수신 대기(ready) 보고 건수 — "보고 수신함" 배지.</summary>
    public int ReadyReportCount(string mainSessionId)
        => Delegations.Count(d => d.MainSessionId == mainSessionId && d.State == DelegationState.Ready);

    /// <summary>
    /// 워커 세션을 풀에 생성(즉시 실행하지 않음). 번역 정책·행동 규칙은 생성 시 고정.
    /// CreateSession과 달리 첫 턴을 돌리지 않는다.
    /// </summary>
    public SessionViewModel CreateWorkerSession(
        EngineDef engine, string model, ProjectViewModel project, string? name,
        bool translationEnabled, string sourceLang, string targetLang, string? behaviorPreamble)
    {
        var title = string.IsNullOrWhiteSpace(name) ? $"{engine.Name} worker" : name.Trim();
        var branch = "worker/" + Slug(title);
        var (reqAppr, sandbox) = PolicyToSession(_approvalPolicy);
        var s = new SessionViewModel(NewSessionId("w"), engine, title, branch, project.Id, project.Name, project.Path, model)
        {
            Role = SessionRole.Worker,
            TranslationEnabled = translationEnabled,
            TranslateSourceLanguage = string.IsNullOrWhiteSpace(sourceLang) ? "Korean" : sourceLang.Trim(),
            TranslateTargetLanguage = string.IsNullOrWhiteSpace(targetLang) ? "English" : targetLang.Trim(),
            BehaviorPreamble = string.IsNullOrWhiteSpace(behaviorPreamble) ? WorkerBehaviorPreamble : behaviorPreamble,
            RequireApproval = reqAppr,
            Sandbox = sandbox,
        };
        s.PropertyChanged += SessionStatusWatch;
        _allSessions.Insert(0, s);
        RefreshProjectSessions(selectFirstIfMissing: false);
        RefreshCounts();
        RefreshProjectCounts();
        SaveState();
        return s;
    }

    /// <summary>
    /// 메인 → 워커 위임 1건: preamble을 부착해 워커 턴을 돌리고, 최종 응답을 보고로 캡처한다.
    /// 워커가 busy면 null(UI가 "유휴 워커 없음" 처리). 반자동: 보고는 ready로 대기, 주입은 사용자 액션.
    /// </summary>
    public async Task<WorkerDelegationViewModel?> DelegateAsync(
        SessionViewModel main, SessionViewModel worker, string prompt, bool sharedWorktree = false)
    {
        if (main is null || worker is null || string.IsNullOrWhiteSpace(prompt)) return null;
        if (IsWorkerBusy(worker)) return null; // busy — UI가 유휴 워커 없음 흐름

        worker.LastMainSessionId = main.Id;
        if (sharedWorktree && main.WorktreePath is { } wt)
        {
            // 메인 worktree 공유(독립 worktree 생성 생략)
            worker.WorktreePath = wt;
            worker.Isolated = main.Isolated;
            worker.WorktreeAttempted = true;
        }

        var d = new WorkerDelegationViewModel(main.Id, worker.Id, prompt)
        {
            SharedWorktree = sharedWorktree,
            WorkerName = worker.Title,
            WorkerAgentId = worker.AgentId,
            WorkerBadge = worker.Badge,
            WorkerModel = worker.Model,
        };
        Delegations.Add(d);
        main.Transcript.Add(new DelegationBlock(d)); // 메인 트랜스크립트에 인라인 카드
        d.State = DelegationState.Running;

        var composed = WorkerDefaults.ComposePrompt(worker.BehaviorPreamble, prompt);
        var costBefore = worker.CostUsd;
        var before = worker.Transcript.OfType<AgentTextBlock>().Count();
        await RunTurnAsync(worker, composed);

        d.CostUsd = Math.Max(0, worker.CostUsd - costBefore);
        // Success = this turn completed and produced a fresh reply (mirrors the task-queue runner).
        // Guards against capturing a stale prior reply when the turn never ran (e.g. concurrency cap).
        var produced = worker.Transcript.OfType<AgentTextBlock>().Count() > before;
        var last = worker.Transcript.OfType<AgentTextBlock>().LastOrDefault();
        if (worker.Status != "done" || !produced || last is null)
        {
            d.Error = worker.Transcript.OfType<ErrorBlock>().LastOrDefault()?.Body
                ?? "워커가 응답을 반환하지 않았습니다.";
            d.State = DelegationState.Failed;
        }
        else
        {
            // 에이전트 간 통신은 원문(엔진 언어) 우선 — 번역 표시본 대신 OriginalText.
            d.Report = last.OriginalText ?? last.Text;
            d.State = DelegationState.Ready;
        }
        SaveState();
        return d;
    }

    /// <summary>단일 보고를 메인 입력(Draft)에 주입. 실행 중 턴엔 넣지 않음(호출부에서 보장).</summary>
    public void InjectReport(SessionViewModel main, WorkerDelegationViewModel d)
    {
        if (main is null || d?.Report is null) return;
        var header = "[Worker report]\n";
        main.Draft = string.IsNullOrWhiteSpace(main.Draft) ? header + d.Report : $"{main.Draft}\n\n{header}{d.Report}";
        d.State = DelegationState.Consumed;
    }

    /// <summary>메인의 ready 보고들을 위임 순서로 병합해 한 번에 주입.</summary>
    public void InjectMergedReports(SessionViewModel main)
    {
        if (main is null) return;
        var ready = Delegations
            .Where(d => d.MainSessionId == main.Id && d.State == DelegationState.Ready)
            .OrderBy(d => d.CreatedAt)
            .ToList();
        if (ready.Count == 0) return;
        var items = ready.Select(d => (WorkerNameFor(d.WorkerSessionId), d.Report ?? "")).ToList();
        var merged = WorkerDefaults.MergeReports(items);
        main.Draft = string.IsNullOrWhiteSpace(main.Draft) ? merged : $"{main.Draft}\n\n{merged}";
        foreach (var d in ready) d.State = DelegationState.Consumed;
    }

    private string WorkerNameFor(string workerSessionId)
        => _allSessions.FirstOrDefault(x => x.Id == workerSessionId)?.Title ?? "worker";
}
