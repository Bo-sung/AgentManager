namespace AgentManager.ViewModels;

public sealed class ProjectViewModel(string id, string name, string path) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Path { get; } = path;

    private int _sessionCount;
    public int SessionCount { get => _sessionCount; set => Set(ref _sessionCount, value); }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}
