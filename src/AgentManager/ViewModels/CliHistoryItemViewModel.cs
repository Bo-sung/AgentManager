using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

/// <summary>사이드바 CLI HISTORY 행: AgentManager 밖에서 돌린 claude/codex 세션 기록.</summary>
public sealed class CliHistoryItemViewModel(CliHistoryEntry entry)
{
    public CliHistoryEntry Entry { get; } = entry;
    public string AgentId => Entry.EngineId; // EngineIcon 스타일 트리거용
    public string Badge => Entry.EngineId switch { "cc" => "CC", "agy" => "AG", _ => "GX" };
    public string Title => Entry.Title;
    public string TimeLabel => Entry.LastWriteUtc.ToLocalTime().ToString("MM-dd HH:mm");
}
