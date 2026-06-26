using AgentManager.Core.Workers;

namespace AgentManager.ViewModels;

/// <summary>메인→워커 위임 1건의 UI 상태(메인 트랜스크립트 DelegationCard 바인딩용).</summary>
public sealed class WorkerDelegationViewModel : ObservableObject
{
    public string Id { get; } = "d" + Guid.NewGuid().ToString("N")[..12]; // fan-out creates these in a tight loop — avoid tick collisions
    public string MainSessionId { get; }
    public string WorkerSessionId { get; }
    /// <summary>메인이 작성한 위임 프롬프트(원문 — preamble 부착 전).</summary>
    public string Prompt { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
    public bool SharedWorktree { get; set; }

    // 카드 표시용 워커 메타(세션 조회 없이 바인딩)
    public string WorkerName { get; init; } = "worker";
    public string WorkerAgentId { get; init; } = "cc";
    public string WorkerBadge { get; init; } = "CC";
    public string WorkerModel { get; init; } = "";

    public WorkerDelegationViewModel(string mainSessionId, string workerSessionId, string prompt)
    {
        MainSessionId = mainSessionId;
        WorkerSessionId = workerSessionId;
        Prompt = prompt;
    }

    private DelegationState _state = DelegationState.Pending;
    public DelegationState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                OnChanged(nameof(IsRunning));
                OnChanged(nameof(IsReady));
                OnChanged(nameof(IsFailed));
                OnChanged(nameof(StatusLabel));
            }
        }
    }
    public bool IsRunning => _state == DelegationState.Running;
    public bool IsReady => _state == DelegationState.Ready;
    public bool IsFailed => _state == DelegationState.Failed;
    public string StatusLabel => _state switch
    {
        DelegationState.Running => AgentManager.App.L("L.DelegRunning"),
        DelegationState.Ready => AgentManager.App.L("L.DelegReady"),
        DelegationState.Failed => AgentManager.App.L("L.DelegFailed"),
        DelegationState.Consumed => AgentManager.App.L("L.DelegConsumed"),
        _ => AgentManager.App.L("L.DelegPending"),
    };

    private string? _report;
    /// <summary>워커 최종 보고(엔진 언어 원문 우선). 완료 시 채워짐.</summary>
    public string? Report { get => _report; set => Set(ref _report, value); }

    private double _costUsd;
    public double CostUsd { get => _costUsd; set => Set(ref _costUsd, value); }

    private string? _error;
    /// <summary>실패 시 오류 요약.</summary>
    public string? Error { get => _error; set => Set(ref _error, value); }
}
