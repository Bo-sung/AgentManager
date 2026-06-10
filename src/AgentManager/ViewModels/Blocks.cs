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
    public string Text
    {
        get => _text;
        set { if (Set(ref _text, value)) OnChanged(nameof(DisplayText)); }
    }

    private string? _originalText;
    public string? OriginalText
    {
        get => _originalText;
        set
        {
            if (Set(ref _originalText, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                OnChanged(nameof(HasOriginal));
                OnChanged(nameof(DisplayText));
            }
        }
    }

    private bool _showOriginal;
    public bool ShowOriginal
    {
        get => _showOriginal;
        set { if (Set(ref _showOriginal, value)) OnChanged(nameof(DisplayText)); }
    }

    public bool HasOriginal => !string.IsNullOrWhiteSpace(_originalText);
    public string DisplayText => ShowOriginal && HasOriginal ? _originalText! : _text;
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
    public string Body
    {
        get => _body;
        set
        {
            if (Set(ref _body, value))
            {
                OnChanged(nameof(HasBody));
                OnChanged(nameof(DisplayBody));
            }
        }
    }
    private string? _originalBody;
    public string? OriginalBody
    {
        get => _originalBody;
        set
        {
            if (Set(ref _originalBody, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                OnChanged(nameof(HasOriginal));
                OnChanged(nameof(DisplayBody));
            }
        }
    }
    private bool _showOriginal;
    public bool ShowOriginal
    {
        get => _showOriginal;
        set { if (Set(ref _showOriginal, value)) OnChanged(nameof(DisplayBody)); }
    }
    public bool HasBody => !string.IsNullOrEmpty(_body);
    public bool HasOriginal => !string.IsNullOrWhiteSpace(_originalBody);
    public string DisplayBody => ShowOriginal && HasOriginal ? _originalBody! : _body;
    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }

    /// <summary>Shell command text (Bash/shell tools) — used for artifact derivation (test runs).</summary>
    public string? CommandText { get; set; }
}

public sealed class ErrorBlock(string title, string body) : TranscriptItem
{
    public string Title { get; } = title;
    public string Body { get; } = body;
}

/// <summary>Model reasoning (Claude thinking block) — collapsed by default.</summary>
public sealed class ThinkingBlock(string text) : TranscriptItem
{
    public string Text { get; } = text;
    private bool _isOpen;
    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }
}

/// <summary>Engine asked permission to run a tool; resolves to allowed/denied/expired.</summary>
public sealed class ApprovalBlock(string requestId, string toolName, string inputSummary) : TranscriptItem
{
    public string RequestId { get; } = requestId;
    public string ToolName { get; } = toolName;
    public string InputSummary { get; } = inputSummary;

    private string _state = "pending"; // pending | allowed | denied | expired
    public string State
    {
        get => _state;
        set { if (Set(ref _state, value)) OnChanged(nameof(IsPending)); }
    }
    public bool IsPending => _state == "pending";
}

public sealed class WorkingBlock : TranscriptItem
{
    private string _text;
    public WorkingBlock(string text) => _text = text;
    public string Text { get => _text; set => Set(ref _text, value); }
}
