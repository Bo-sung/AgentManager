using System.IO;
using System.Text.Json;
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
}

public sealed record AppSettingsDto
{
    public string ClaudePath { get; init; } = "";
    public string CodexPath { get; init; } = "";
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";
    public string OllamaModel { get; init; } = "exaone3.5:7.8b";
    public bool TranslationEnabled { get; init; } = true;
    public int MaxConcurrentSessions { get; init; } = 3;
    /// <summary>워커 전용 동시 실행 cap(메인 cap과 분리). 워커 위임 병렬성.</summary>
    public int MaxConcurrentWorkers { get; init; } = 2;
    /// <summary>새 워커 생성 시 기본으로 채워지는 전역 행동 규칙 preamble 템플릿.</summary>
    public string WorkerBehaviorPreamble { get; init; } = "";
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
    /// <summary>밀도: comfortable | compact (UI 스케일).</summary>
    public string Density { get; init; } = "comfortable";
    /// <summary>익명 텔레메트리 opt-in (로컬 전용, 외부 전송 없음).</summary>
    public bool Telemetry { get; init; }
    /// <summary>사용자가 비활성한 엔진 id 목록 (New Agent 피커에서 숨김).</summary>
    public List<string> DisabledEngines { get; init; } = [];
    /// <summary>엔진별 인증 모드: subscription(CLI 로그인) | api (engineId → mode).</summary>
    public Dictionary<string, string> EngineAuthMode { get; init; } = new();
    /// <summary>엔진별 API 키 (DPAPI 암호화 base64, engineId → blob). 평문 저장 금지.</summary>
    public Dictionary<string, string> EngineApiKey { get; init; } = new();
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
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<AppStateDto>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(AppStateDto state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, StatePath, overwrite: true);
    }

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
