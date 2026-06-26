using System.IO;
using System.Text.Json;
using AgentManager.Core;
using AgentManager.ViewModels;

namespace AgentManager.Persistence;

public sealed record AppStateDto
{
    public string? ActiveProjectId { get; init; }
    /// <summary>설정은 settings.json(SettingsStore)으로 분리 저장 — state.json에는 직렬화하지 않는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public AppSettingsDto Settings { get; init; } = new();
    public List<ProjectDto> Projects { get; init; } = [];
    public List<SessionDto> Sessions { get; init; } = [];
    /// <summary>워커 작업 백로그/큐(프로젝트 단위). 스킬이 스풀로 등록 → 유저가 워커에 할당.</summary>
    public List<AgentManager.Core.Workers.WorkerTaskDto> WorkerTasks { get; init; } = [];
}

public sealed record AppSettingsDto
{
    public string ClaudePath { get; init; } = "";
    public string CodexPath { get; init; } = "";
    public string AgyPath { get; init; } = "";
    public string PiPath { get; init; } = "";
    public Dictionary<string, string[]> PreferredModels { get; init; } = new();   // 엔진별 "주로 쓰는 모델" 체크 집합(cc/gx/agy/pi)
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";
    public string OllamaModel { get; init; } = "exaone3.5:7.8b";
    public bool TranslationEnabled { get; init; } = true;
    public int MaxConcurrentSessions { get; init; } = 3;
    /// <summary>워커 전용 동시 실행 cap(메인 cap과 분리). 워커 위임 병렬성.</summary>
    public int MaxConcurrentWorkers { get; init; } = 2;
    /// <summary>새 워커 생성 시 기본으로 채워지는 전역 행동 규칙 preamble 템플릿.</summary>
    public string WorkerBehaviorPreamble { get; init; } = "";
    /// <summary>각 엔진 스킬 폴더에 주입할 SKILL.md 내용(빈 값 = 기본 워커-프롬프트 템플릿).</summary>
    public string SkillContent { get; init; } = "";
    /// <summary>엔진별 스킬 설치 폴더(engineId → 경로). 비어있으면 SkillInjector 기본 경로.</summary>
    public Dictionary<string, string> SkillDirs { get; init; } = new();
    public bool ReviewPaneOpen { get; init; } = true;
    /// <summary>비-git 폴더에서 "격리 없이 실행" 안내 표시 여부 (기본 끔).</summary>
    public bool WarnNoWorktree { get; init; }
    /// <summary>UI 테마: dark | light (재시작 시 적용).</summary>
    public string Theme { get; init; } = "dark";
    /// <summary>UI language: ko | en (applies after restart).</summary>
    public string Language { get; init; } = "ko";
    /// <summary>번역 전 언어(사용자 입력·표시) — 영어 표기. 예: Korean.</summary>
    public string TranslateSourceLanguage { get; init; } = "Korean";
    /// <summary>번역 후 언어(엔진 전달, 토큰 절감) — 영어 표기. 예: English.</summary>
    public string TranslateTargetLanguage { get; init; } = "English";
    /// <summary>새 세션 기본 승인 정책: ask | safe | yolo (RequireApproval + Sandbox 시드).</summary>
    public string ApprovalPolicy { get; init; } = "yolo";
    /// <summary>worktree 격리 기준 경로 (빈 값 = 기본 앱 데이터 폴더).</summary>
    public string WorktreeBase { get; init; } = "";
    /// <summary>시작 시 마지막 활성 세션을 자동으로 연다.</summary>
    public bool AutoStartLastSession { get; init; }
    /// <summary>목록(사이드바/대시보드)에 실시간 활동을 표시한다.</summary>
    public bool StreamLogs { get; init; } = true;
    /// <summary>엔진별 새 세션 기본 모델 (engineId → model). 없으면 엔진의 첫 모델 사용.</summary>
    public Dictionary<string, string> DefaultModels { get; init; } = new();
    /// <summary>강조색 프리셋: ember | amber | teal | azure | violet (라이브 적용).</summary>
    public string Accent { get; init; } = "ember";
    /// <summary>밀도(레거시): comfortable | compact. UiScale로 대체됨 — 마이그레이션 읽기용으로만 유지.</summary>
    public string Density { get; init; } = "comfortable";
    /// <summary>본문 줌 배율(Ctrl+휠). 0.5~2.0. 0 = 미설정(마이그레이션용).</summary>
    public double BodyScale { get; init; }
    /// <summary>모달 줌 배율(본문과 독립). 0.5~2.0. 0 = 미설정.</summary>
    public double ModalScale { get; init; }
    /// <summary>(레거시) 통합 줌 배율 — BodyScale/ModalScale로 대체, 마이그레이션 읽기용.</summary>
    public double UiScale { get; init; }
    /// <summary>(레거시) 줌 범위 all|body — 마이그레이션 읽기용.</summary>
    public string ZoomScope { get; init; } = "all";
    /// <summary>익명 텔레메트리 opt-in (로컬 전용, 외부 전송 없음).</summary>
    public bool Telemetry { get; init; }
    /// <summary>사용자가 비활성한 엔진 id 목록 (New Agent 피커에서 숨김).</summary>
    public List<string> DisabledEngines { get; init; } = [];
    /// <summary>사용자가 삭제한 CLI 세션 id — CLI History 재발견에서 영구 제외.</summary>
    public List<string> DismissedCliSessions { get; init; } = [];
    /// <summary>엔진별 인증 모드: subscription(CLI 로그인) | api (engineId → mode).</summary>
    public Dictionary<string, string> EngineAuthMode { get; init; } = new();
    /// <summary>엔진별 API 키 (DPAPI 암호화 base64, engineId → blob). 평문 저장 금지.</summary>
    public Dictionary<string, string> EngineApiKey { get; init; } = new();
    /// <summary>엔진별 "한도 도달 시 API 자동 전환" 토글(opt-in).</summary>
    public Dictionary<string, bool> EngineAutoApiOnLimit { get; init; } = new();
    /// <summary>엔진별 rate-limit 차단 해제 시각(unix). 실제 실패 시 기록 — 재시작 후에도 소진 상태 유지.</summary>
    public Dictionary<string, long> EngineLimitedUntil { get; init; } = new();
    /// <summary>엔진별 마지막 사용량(rate-limit) 스냅샷. 재시작 후 footer 복원용 — 신선도 라벨과 함께 표시.</summary>
    public Dictionary<string, UsageSnapshotDto> Usage { get; init; } = new();
}

public sealed record UsageSnapshotDto
{
    public double Utilization { get; init; }
    public long ResetsAtUnix { get; init; }
    public string RateLimitType { get; init; } = "";
    public DateTime CapturedUtc { get; init; }
}

public sealed record ProjectDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    /// <summary>MCP passthrough: user-managed mcp config file → claude --mcp-config.</summary>
    public string McpConfigPath { get; init; } = "";
    /// <summary>멀티폴더 project: 주 폴더 외 추가 루트.</summary>
    public List<string> ExtraPaths { get; init; } = [];
}

public sealed record ArtifactDto
{
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public bool IsError { get; init; }
}

public sealed record SessionDto
{
    public required string Id { get; init; }
    public required string AgentId { get; init; }
    public required string Title { get; init; }
    public required string Branch { get; init; }
    public required string ProjectId { get; init; }
    public required string Project { get; init; }
    public required string ProjectPath { get; init; }
    public required string Model { get; init; }
    public bool? TranslationEnabled { get; init; }
    public string? EngineSessionId { get; init; }
    /// <summary>세션 역할: Plain | Main | Worker (워커 위임). 빈값 = Plain.</summary>
    public string Role { get; init; } = "Plain";
    /// <summary>워커 고정 행동 규칙(위임 프롬프트 앞 부착). 워커일 때만.</summary>
    public string BehaviorPreamble { get; init; } = "";
    /// <summary>워커 고정 번역 전 언어. null = 전역 설정.</summary>
    public string? TranslateSourceLanguage { get; init; }
    /// <summary>워커 고정 번역 후 언어. null = 전역 설정.</summary>
    public string? TranslateTargetLanguage { get; init; }
    /// <summary>워커의 마지막 담당 메인 세션 id.</summary>
    public string? LastMainSessionId { get; init; }
    public required string Status { get; init; }
    public string Activity { get; init; } = "";
    public long TokensIn { get; init; }
    public long TokensOut { get; init; }
    public double CostUsd { get; init; }
    public bool IsArchived { get; init; }
    public string Sandbox { get; init; } = "DangerFullAccess";
    public bool RequireApproval { get; init; }
    /// <summary>코덱스 추론 강도 (low/medium/high/xhigh, 빈값 = 기본).</summary>
    public string ReasoningEffort { get; init; } = "";
    public List<ArtifactDto> Artifacts { get; init; } = [];
    public DateTime StartedAt { get; init; }
    public string? WorktreePath { get; init; }
    public bool Isolated { get; init; }
    public bool WorktreeAttempted { get; init; }
    public List<TranscriptDto> Transcript { get; init; } = [];
}

public sealed record TranscriptDto
{
    public required string Type { get; init; }
    public string Text { get; init; } = "";
    public string OriginalText { get; init; } = "";
    public string SentText { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string OriginalBody { get; init; } = "";
    public string ToolUseId { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string Stat { get; init; } = "";
    public bool IsOpen { get; init; }
    public bool ShowOriginal { get; init; }
}

public static class AppStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static string StatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "state.json");

    public static AppStateDto? Load()
        => JsonFile.ReadOrDefault<AppStateDto?>(StatePath, () => null, Options);

    public static void Save(AppStateDto state)
        => JsonFile.WriteAtomic(StatePath, state, Options);

    public static TranscriptDto ToDto(TranscriptItem item) => item switch
    {
        UserBlock u => new TranscriptDto { Type = "user", Text = u.Text, SentText = u.SentText ?? "" },
        AgentTextBlock a => new TranscriptDto { Type = "agent", Text = a.Text, OriginalText = a.OriginalText ?? "", ShowOriginal = a.ShowOriginal, Name = a.ModelUsed ?? "" },
        ToolBlock t => new TranscriptDto
        {
            Type = "tool",
            ToolUseId = t.ToolUseId,
            Kind = t.Kind,
            Name = t.Name,
            Stat = t.Stat,
            Body = t.Body,
            OriginalBody = t.OriginalBody ?? "",
            IsOpen = t.IsOpen,
            ShowOriginal = t.ShowOriginal,
        },
        ErrorBlock e => new TranscriptDto { Type = "error", Title = e.Title, Body = e.Body },
        WorkingBlock w => new TranscriptDto { Type = "working", Text = w.Text },
        ThinkingBlock th => new TranscriptDto { Type = "thinking", Text = th.Text },
        ApprovalBlock p => new TranscriptDto { Type = "approval", ToolUseId = p.RequestId, Name = p.ToolName, Body = p.InputSummary, Stat = p.State },
        _ => new TranscriptDto { Type = "unknown" },
    };

    public static TranscriptItem FromDto(TranscriptDto dto) => dto.Type switch
    {
        "user" => new UserBlock(dto.Text) { SentText = dto.SentText },
        "agent" => new AgentTextBlock(dto.Text) { OriginalText = dto.OriginalText, ShowOriginal = dto.ShowOriginal, ModelUsed = dto.Name },
        "tool" => new ToolBlock(dto.ToolUseId, dto.Kind, dto.Name)
        {
            Stat = dto.Stat,
            Body = dto.Body,
            OriginalBody = dto.OriginalBody,
            IsOpen = dto.IsOpen,
            ShowOriginal = dto.ShowOriginal,
        },
        "error" => new ErrorBlock(dto.Title, dto.Body),
        "working" => new WorkingBlock(dto.Text),
        "thinking" => new ThinkingBlock(dto.Text),
        // pending approvals can't survive a restart — the engine is gone
        "approval" => new ApprovalBlock(dto.ToolUseId, dto.Name, dto.Body) { State = dto.Stat == "pending" ? "expired" : dto.Stat },
        _ => new WorkingBlock(AgentManager.App.L("L.UnrestorableTranscriptBlock")),
    };
}
