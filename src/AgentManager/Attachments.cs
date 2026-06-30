using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgentManager;

/// <summary>Classifies composer attachments and renders documents as fenced prompt text.
/// Images go to the engine as base64 blocks (SessionOptions.Images); documents are inlined.</summary>
public static class Attachments
{
    static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>Binary office/PDF formats. Inlined text fails for these, so they are delivered by
    /// PATH PASS-THROUGH (copied under cwd/.am/attachments and referenced in the prompt) and the
    /// agent parses them with its own tools. Verified: cc/gx/pi read docx/xlsx/pptx/pdf this way.</summary>
    static readonly HashSet<string> BinaryDocExt = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".docx", ".xlsx", ".pptx", ".doc", ".xls", ".ppt" };

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));

    /// <summary>Binary office/PDF document — delivered by path pass-through, not inlined text.</summary>
    public static bool IsBinaryDoc(string path) => BinaryDocExt.Contains(Path.GetExtension(path));

    const long MaxDocBytes = 512 * 1024; // cap inlined text per file

    /// <summary>Above this size a TEXT doc is delivered by PATH PASS-THROUGH instead of inlined. A large
    /// inlined block bloats every turn's prompt (re-sent each turn = token waste) and — critically — makes
    /// some engines stall: measured, cc in stream-json input mode delays its init by ~90s on a 60KB
    /// inlined user message (a 19-byte message inits in 6s; pi tolerates the large one). Small snippets
    /// still inline for convenience; anything substantial is referenced by path and the agent Reads it.</summary>
    const long MaxInlineBytes = 16 * 1024;

    /// <summary>Deliver this doc by path pass-through (copied + referenced) rather than inlining its text?
    /// True for binary office/PDF formats, and for any text doc larger than <see cref="MaxInlineBytes"/>.</summary>
    public static bool PassThrough(string path)
    {
        if (IsBinaryDoc(path)) return true;
        try { return new FileInfo(path).Length > MaxInlineBytes; } catch { return false; }
    }

    /// <summary>If the composer prompt itself is large enough to stall the engine's stdin (cc in
    /// stream-json input mode degrades super-linearly — a 60KB user message delays its init ~90s; a few
    /// hundred KB → many minutes), write it to <c>&lt;cwd&gt;/.am/attachments/prompt-*.md</c> and return a
    /// short reference for the agent to Read, keeping the stdin small. Small prompts pass through unchanged.
    /// Never throws — falls back to the inline prompt. Returns (promptToSend, savedFile or null).</summary>
    public static (string Prompt, string? SavedFile) OffloadLargePrompt(string prompt, string cwd)
    {
        if (string.IsNullOrEmpty(prompt) || Encoding.UTF8.GetByteCount(prompt) <= MaxInlineBytes)
            return (prompt, null);
        try
        {
            var dir = Path.Combine(Path.GetFullPath(cwd), ".am", "attachments");
            Directory.CreateDirectory(dir);
            var file = Dedupe(Path.Combine(dir, $"prompt-{DateTime.Now:yyyyMMdd-HHmmss}.md"));
            System.IO.File.WriteAllText(file, prompt, new UTF8Encoding(false));
            var reference = "The instruction below was too large to send inline, so it was saved to a file. "
                + "Read this file and follow its full contents as the request:\n" + file;
            return (reference, file);
        }
        catch { return (prompt, null); }
    }

    /// <summary>Render the given document paths as fenced blocks for the prompt. Empty if none.</summary>
    public static string BuildDocsText(IReadOnlyList<string>? docs)
    {
        if (docs is null || docs.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var d in docs)
        {
            var block = ReadDocBlock(d);
            if (block.Length > 0) sb.Append(block).Append("\n\n");
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Append a compact "📎 a.md, b.png" note to the user's transcript text.</summary>
    public static string DisplayNote(string prompt, IEnumerable<string> attachmentPaths)
    {
        var names = attachmentPaths.Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).ToList();
        if (names.Count == 0) return prompt;
        var note = "📎 " + string.Join(", ", names);
        return string.IsNullOrEmpty(prompt) ? note : prompt + "\n\n" + note;
    }

    /// <summary>Copy <paramref name="source"/> into <c>&lt;cwd&gt;/.am/attachments/&lt;filename&gt;</c>
    /// (deduped on name collisions) so the agent can read it from its working directory. If the
    /// file already lives under <paramref name="cwd"/> it is referenced in place (no copy).
    /// Returns the absolute path to reference and whether the path is usable: true when copied OR
    /// when referenced in place; false on copy failure (caller falls back to the original path).</summary>
    public static (string RefPath, bool Ok) CopyToAttachmentsDir(string source, string cwd)
    {
        try
        {
            var fullSrc = Path.GetFullPath(source);
            var fullCwd = Path.GetFullPath(cwd);
            var cwdRoot = fullCwd.EndsWith(Path.DirectorySeparatorChar) || fullCwd.EndsWith(Path.AltDirectorySeparatorChar)
                ? fullCwd : fullCwd + Path.DirectorySeparatorChar;
            // Already inside cwd → reference as-is (agent reads it in place).
            if (fullSrc.Equals(fullCwd, StringComparison.OrdinalIgnoreCase) ||
                fullSrc.StartsWith(cwdRoot, StringComparison.OrdinalIgnoreCase))
                return (fullSrc, true);

            var dir = Path.Combine(fullCwd, ".am", "attachments");
            Directory.CreateDirectory(dir);
            var dest = Dedupe(Path.Combine(dir, Path.GetFileName(fullSrc)));
            File.Copy(fullSrc, dest, overwrite: true);
            return (dest, true);
        }
        catch
        {
            // Fall back to the original absolute path; caller emits a warning. Never throw.
            try { return (Path.GetFullPath(source), false); } catch { return (source, false); }
        }
    }

    static string Dedupe(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>One prompt line telling the agent to read a pass-through attachment. The agent parses
    /// it with its own tools (docx/xlsx/pptx/pdf) — we never inline bytes that fail to inline.</summary>
    public static string BuildAttachedRef(string absolutePath)
        => $"첨부 파일(읽어서 참고): {absolutePath}";

    static string ReadDocBlock(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return "";
            using var fs = fi.OpenRead();
            var len = (int)Math.Min(fi.Length, MaxDocBytes);
            var bytes = new byte[len];
            int read = fs.Read(bytes, 0, len);
            if (LooksBinary(bytes, read))
                return $"## Attached file: {fi.Name} (binary, not inlined — {path})";

            var text = new UTF8Encoding(false).GetString(bytes, 0, read);
            var truncated = fi.Length > MaxDocBytes ? "\n… (truncated)" : "";
            // Use a fence longer than any backtick run in the content so embedded code
            // fences (common in markdown) can't terminate our block early.
            var fence = new string('`', Math.Max(3, LongestBacktickRun(text) + 1));
            var lang = LangFromExt(Path.GetExtension(path));
            return $"## Attached file: {fi.Name}\n{fence}{lang}\n{text}{truncated}\n{fence}";
        }
        catch { return ""; }
    }

    static bool LooksBinary(byte[] b, int len)
    {
        int n = Math.Min(len, 8000);
        for (int i = 0; i < n; i++) if (b[i] == 0) return true;
        return false;
    }

    static int LongestBacktickRun(string s)
    {
        int max = 0, cur = 0;
        foreach (var c in s)
        {
            if (c == '`') { cur++; if (cur > max) max = cur; }
            else cur = 0;
        }
        return max;
    }

    static string LangFromExt(string ext) => ext.ToLowerInvariant() switch
    {
        ".md" or ".markdown" => "markdown",
        ".cs" => "csharp",
        ".js" or ".jsx" or ".mjs" => "javascript",
        ".ts" or ".tsx" => "typescript",
        ".py" => "python",
        ".json" => "json",
        ".xml" or ".xaml" or ".csproj" or ".props" or ".targets" => "xml",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".yml" or ".yaml" => "yaml",
        ".sh" or ".bash" => "bash",
        ".ps1" => "powershell",
        ".sql" => "sql",
        ".java" => "java",
        ".go" => "go",
        ".rs" => "rust",
        ".rb" => "ruby",
        ".php" => "php",
        ".c" or ".h" => "c",
        ".cpp" or ".hpp" or ".cc" or ".cxx" => "cpp",
        ".toml" => "toml",
        ".ini" or ".cfg" or ".conf" => "ini",
        _ => "",
    };
}
