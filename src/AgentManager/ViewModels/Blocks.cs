namespace AgentManager.ViewModels;

/// <summary>Base for a transcript item (rendered via per-type DataTemplate).</summary>
public abstract class TranscriptItem : ObservableObject { }

public sealed class UserBlock(string text) : TranscriptItem
{
    public string Text { get; } = text;
}

public sealed class AgentTextBlock : TranscriptItem
{
    private string _text;
    public AgentTextBlock(string text) => _text = text;
    public string Text { get => _text; set => Set(ref _text, value); }
}

public sealed class ToolBlock : TranscriptItem
{
    public string ToolUseId { get; }
    public string Kind { get; }        // READ / EDIT / RUN
    public string Name { get; }
    private string _stat = "…";
    private string _body = "";
    private bool _isOpen;
    public ToolBlock(string toolUseId, string kind, string name)
    {
        ToolUseId = toolUseId; Kind = kind; Name = name;
    }
    public string Stat { get => _stat; set => Set(ref _stat, value); }
    public string Body { get => _body; set { if (Set(ref _body, value)) OnChanged(nameof(HasBody)); } }
    public bool HasBody => !string.IsNullOrEmpty(_body);
    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }
}

public sealed class ErrorBlock(string title, string body) : TranscriptItem
{
    public string Title { get; } = title;
    public string Body { get; } = body;
}

public sealed class WorkingBlock : TranscriptItem
{
    private string _text;
    public WorkingBlock(string text) => _text = text;
    public string Text { get => _text; set => Set(ref _text, value); }
}
