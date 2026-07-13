namespace AgentManager.Core.Workers;

/// <summary>세션 역할. Plain = 일반 세션(기본), Main = 위임을 발신, Worker = 위임을 수행.</summary>
public enum SessionRole
{
    Plain,
    Main,
    Worker,
}

/// <summary>메인→워커 위임 1건의 상태.</summary>
public enum DelegationState
{
    /// <summary>전송 준비/대기.</summary>
    Pending,
    /// <summary>워커 실행 중.</summary>
    Running,
    /// <summary>완료 — 보고 수신 대기(메인 미주입).</summary>
    Ready,
    /// <summary>워커 실패/취소.</summary>
    Failed,
    /// <summary>보고가 메인에 주입됨.</summary>
    Consumed,
}

/// <summary>
/// 메인 세션이 워커 세션에게 보낸 위임 1건. 순수 데이터(UI 비의존).
/// 워커 자체는 평범한 세션(SessionRole.Worker)이며, 워커별 고정 설정
/// (모델·번역정책·행동규칙)은 세션에 저장된다.
/// </summary>
public sealed record WorkerDelegation
{
    public required string Id { get; init; }
    public required string MainSessionId { get; init; }
    public required string WorkerSessionId { get; init; }
    /// <summary>메인이 작성한 위임 프롬프트(워커 preamble 부착 전 원문).</summary>
    public required string Prompt { get; init; }
    /// <summary>워커 최종 보고(완료 시). 없으면 미완.</summary>
    public string? Report { get; init; }
    public DelegationState State { get; init; } = DelegationState.Pending;
    /// <summary>이 위임으로 워커가 소비한 비용(USD).</summary>
    public double CostUsd { get; init; }
    /// <summary>워커가 메인 worktree를 공유했는지(false = 독립 worktree).</summary>
    public bool SharedWorktree { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>워커 위임 기본값.</summary>
public static class WorkerDefaults
{
    /// <summary>새 워커의 기본 행동 규칙(위임 프롬프트 앞에 부착). 사용자가 Settings/생성 시 편집 가능.</summary>
    public const string BehaviorPreamble =
        "You are a worker agent. Do exactly the delegated task, concisely. " +
        "If anything is unclear, make a reasonable assumption and proceed — do not ask back. " +
        "When you have finished the task, write your final report (3-6 lines: what you did + the result/artifacts) " +
        "to a file named report.md inside the directory given by the AGENTMANAGER_REPORT_SPOOL environment variable — " +
        "that is how your report reaches the control tower's report inbox, so send it as soon as the work is done. " +
        "Also end your chat response with the same \"## Report\" section (a fallback if the variable is unset).";

    /// <summary>워커 전용 동시 실행 기본 cap(메인 cap과 분리).</summary>
    public const int DefaultMaxConcurrentWorkers = 2;

    /// <summary>위임 프롬프트 조립: 워커 preamble + 작업.</summary>
    public static string ComposePrompt(string behaviorPreamble, string task)
    {
        var preamble = (behaviorPreamble ?? "").Trim();
        var body = (task ?? "").Trim();
        return string.IsNullOrEmpty(preamble) ? body : $"{preamble}\n\n## Task\n{body}";
    }

    /// <summary>준비된 보고들을 위임 순서로 워커 라벨과 함께 병합(다수 워커 합쳐 붙이기).</summary>
    public static string MergeReports(IReadOnlyList<(string Worker, string Report)> reports)
    {
        if (reports is null || reports.Count == 0) return "";
        if (reports.Count == 1) return reports[0].Report ?? "";
        return string.Join("\n\n", reports.Select((r, i) => $"## Worker {i + 1} ({r.Worker})\n{r.Report}"));
    }
}
