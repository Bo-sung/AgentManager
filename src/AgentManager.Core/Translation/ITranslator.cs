namespace AgentManager.Core.Translation;

public enum TranslationDirection
{
    /// <summary>Korean user input → English (before sending to the engine; cuts engine tokens).</summary>
    KoToEn,
    /// <summary>English engine output → Korean (display only).</summary>
    EnToKo,
}

/// <summary>Local-LLM translation layer. Failures must fall back to the original text.</summary>
public interface ITranslator
{
    Task<string> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct = default);
    bool ContainsKorean(string text);
}
