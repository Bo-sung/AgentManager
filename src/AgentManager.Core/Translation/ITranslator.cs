namespace AgentManager.Core.Translation;

public enum TranslationDirection
{
    /// <summary>User input (source language) → engine (target language); before sending, cuts engine tokens.</summary>
    SourceToTarget,
    /// <summary>Engine output (target language) → user (source language); display only.</summary>
    TargetToSource,
}

/// <summary>Local-LLM translation layer. Failures must fall back to the original text.</summary>
public interface ITranslator
{
    Task<string> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct = default);
    bool ContainsKorean(string text);
}
