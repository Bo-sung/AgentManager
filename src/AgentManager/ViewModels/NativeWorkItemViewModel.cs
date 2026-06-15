using AgentManager.Core.Observation;

namespace AgentManager.ViewModels;

public sealed class NativeWorkItemViewModel : ObservableObject
{
    public string Id { get; }

    public NativeWorkItemViewModel(ObservedWorkItem item)
    {
        Id = item.Id;
        Update(item);
    }

    private string _title = "";
    public string Title { get => _title; private set => Set(ref _title, value); }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string _detail = "";
    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    private string _source = "";
    public string Source { get => _source; private set => Set(ref _source, value); }

    private string _kind = "";
    public string Kind { get => _kind; private set => Set(ref _kind, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    private bool _isCompleted;
    public bool IsCompleted { get => _isCompleted; private set => Set(ref _isCompleted, value); }

    private bool _isFailed;
    public bool IsFailed { get => _isFailed; private set => Set(ref _isFailed, value); }

    public void Update(ObservedWorkItem item)
    {
        Title = item.DisplayName
            ?? item.AgentType
            ?? item.AgentId
            ?? item.VendorWorkId
            ?? item.Kind.ToString();
        Status = item.State.ToString();
        Kind = item.Kind.ToString();
        Source = $"{item.Source} / {item.Confidence}";
        Detail = item.LastMessage
            ?? item.AgentTranscriptPath
            ?? item.TranscriptPath
            ?? item.Cwd
            ?? item.VendorParentSessionId
            ?? "";
        IsRunning = item.State is ObservedState.Starting or ObservedState.Running or ObservedState.Waiting or ObservedState.WaitingPermission;
        IsCompleted = item.State == ObservedState.Completed;
        IsFailed = item.State is ObservedState.Failed or ObservedState.Stopped;
    }
}
