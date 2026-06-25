namespace AgentManager.ViewModels;

/// <summary>A file queued for the next turn. Images are sent to the engine as base64 image
/// blocks; documents are inlined into the prompt as fenced text. Not persisted.</summary>
public sealed record PendingAttachment(string Path, bool IsImage)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}
