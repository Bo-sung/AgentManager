using System.Text.RegularExpressions;

namespace AgentManager.Core;

/// <summary>Pure markdown text fix-ups applied before rendering. Kept in Core (no WPF dependency) so the
/// tricky string logic is unit-testable by the smoke runner.</summary>
public static partial class MarkdownText
{
    /// <summary>Put a code-fence opener that got glued to the end of a text line onto its own line, so
    /// the fence pairs correctly. Translation often joins a fence onto the previous line
    /// (e.g. <c>설명: ```powershell</c>): the opener is then no longer at line start, so it isn't
    /// recognized, the matching <c>```</c> looks like a stray opener, and the rest of the message gets
    /// swallowed as one code block. Only a fence carrying a language tag at end-of-line is split — that
    /// is an unambiguous opener; inline <c>`code`</c> (single backticks) and clean line-start fences are
    /// left untouched.</summary>
    public static string SplitGluedFences(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return GluedFenceRx().Replace(text, "$1\n$2");
    }

    // <line text ending in non-space><spaces>```<language>  (nothing after) → newline before the fence.
    [GeneratedRegex(@"(?m)^(.*\S)[ \t]+(`{3,}[A-Za-z][\w+#.-]*)[ \t]*$")]
    private static partial Regex GluedFenceRx();
}
