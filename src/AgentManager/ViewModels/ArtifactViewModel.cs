namespace AgentManager.ViewModels;

/// <summary>
/// Lightweight evidence artifact derived from normalized events (no extra engine
/// calls): tasklist ← TodoWrite input, test ← test-runner command results,
/// summary ← the turn's final assistant text.
/// </summary>
public sealed class ArtifactViewModel(string kind, string title) : ObservableObject
{
    public string Kind { get; } = kind;   // tasklist | test | summary
    public string Title { get; } = title;

    private string _content = "";
    public string Content { get => _content; set { if (Set(ref _content, value)) Touch(); } }

    private bool _isError;
    public bool IsError { get => _isError; set => Set(ref _isError, value); }

    private DateTime _updatedAt = DateTime.Now;
    public DateTime UpdatedAt { get => _updatedAt; private set => Set(ref _updatedAt, value); }
    private void Touch() => UpdatedAt = DateTime.Now;
}
