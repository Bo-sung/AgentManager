namespace AgentManager.ViewModels;

public sealed class ProjectViewModel(string id, string name, string path) : ObservableObject
{
    public string Id { get; } = id;

    private string _name = name;
    public string Name { get => _name; set => Set(ref _name, value); }

    public string Path { get; } = path;

    private int _sessionCount;
    public int SessionCount { get => _sessionCount; set => Set(ref _sessionCount, value); }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>이름 변경 입력 바인딩(메뉴의 Rename TextBox). 커맨드가 이 값을 읽는다.</summary>
    private string _renameDraft = "";
    public string RenameDraft { get => _renameDraft; set => Set(ref _renameDraft, value); }

    /// <summary>MCP 패스스루: 사용자가 관리하는 mcp 설정 파일 경로(.mcp.json 등) → claude --mcp-config.</summary>
    private string _mcpConfigPath = "";
    public string McpConfigPath { get => _mcpConfigPath; set => Set(ref _mcpConfigPath, value); }

    /// <summary>멀티폴더 project: 주 폴더 외 에이전트가 접근할 추가 루트 (claude --add-dir 등).</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> ExtraPaths { get; } = [];
}
