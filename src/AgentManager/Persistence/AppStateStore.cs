using System.IO;
using System.Text.Json;
using AgentManager.ViewModels;

namespace AgentManager.Persistence;

public sealed record AppStateDto
{
    public string? ActiveProjectId { get; init; }
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
    public bool ReviewPaneOpen { get; init; } = true;
    /// <summary>비-git 폴더에서 "격리 없이 실행" 안내 표시 여부 (기본 끔).</summary>
    public bool WarnNoWorktree { get; init; }
    /// <summary>UI 테마: dark | light (재시작 시 적용).</summary>
    public string Theme { get; init; } = "dark";
    /// <summary>UI language: ko | en (applies after restart).</summary>
    public string Language { get; init; } = "ko";
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
