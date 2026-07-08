namespace AgentManager.Core.Translation;

public enum TranslationDirection
{
    /// <summary>User input (source language) → engine (target language); before sending, cuts engine tokens.</summary>
    SourceToTarget,
    /// <summary>Engine output (target language) → user (source language); display only.</summary>
    TargetToSource,
}

/// <summary>Why a translation call returned the text it did — lets the UI tell a real FAILURE (timeout/error →
/// original returned) apart from a legitimate SKIP (text already in the target language). A failure is surfaced;
/// a skip is not.</summary>
public enum TranslateStatus { Translated, Skipped, Failed }

/// <summary>Result of a translation attempt: the (possibly original) text + why.</summary>
public readonly record struct TranslateOutcome(string Text, TranslateStatus Status);

/// <summary>Local-LLM translation layer. Failures must fall back to the original text.</summary>
public interface ITranslator
{
    Task<string> TranslateAsync(string text, TranslationDirection direction, CancellationToken ct = default);
    /// <summary>Same as <see cref="TranslateAsync"/> but reports whether it translated, skipped, or FAILED
    /// (timeout/provider error) — so a caller can notify the user instead of silently showing the original.</summary>
    Task<TranslateOutcome> TranslateWithOutcomeAsync(string text, TranslationDirection direction, CancellationToken ct = default);
    bool ContainsKorean(string text);
}
