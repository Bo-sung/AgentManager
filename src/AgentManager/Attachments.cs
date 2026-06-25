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

    public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path));

    const long MaxDocBytes = 512 * 1024; // cap inlined text per file

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
