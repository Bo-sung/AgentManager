using AgentManager.Core.Workspace;

namespace AgentManager.ViewModels;

public sealed class ReviewChangeViewModel(FileChange change) : ObservableObject
{
    public string Path { get; } = change.Path;
    public ChangeKind Kind { get; } = change.Kind;
    public int Added { get; } = change.Added;
    public int Deleted { get; } = change.Deleted;

    public string KindLabel => Kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Untracked => "U",
        _ => "M",
    };

    public string StatLabel => $"+{Added} / -{Deleted}";
}
