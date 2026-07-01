using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentManager.Core.Translation;

namespace AgentManager.Core;

/// <summary>
/// Pure, stateless, WPF-free helpers shared by the GUI ViewModels (and, later, a headless CLI).
/// Relocated from <c>AppViewModel.*.cs</c> in overhaul (a) Step 0, with behavior kept identical.
/// The VMs keep thin static forwards so existing call sites stay untouched.
///
/// Nothing here touches a WPF type, <c>Application.Current</c>, a Dispatcher, or any VM state.
/// </summary>
public static class CoreHelpers
{
    // ----- artifact / tool classification -----

    /// <summary>Heuristic: does this shell command look like a test run? Used to derive a test artifact.</summary>
    public static bool IsTestCommand(string cmd) =>
        Regex.IsMatch(cmd,
            @"\b(dotnet\s+test|npm\s+(run\s+)?test|pytest|vitest|jest|mocha|cargo\s+test|go\s+test)\b",
            RegexOptions.IgnoreCase);

    /// <summary>Classify a tool name into a coarse READ/EDIT/RUN bucket for the transcript row.</summary>
    public static string KindOf(string name) => name switch
    {
        "Read" or "Glob" or "Grep" or "LS" => "READ",
        "Edit" or "MultiEdit" or "Write" => "EDIT",
        _ => "RUN",
    };

    /// <summary>Trim a string to <paramref name="max"/> chars, appending an ellipsis if cut.</summary>
    public static string Trim(string s, int max) => s.Length > max ? s[..max] + "\u2026" : s;

    /// <summary>Build a filesystem/git-safe slug (lowercased, non-alnum to '-', collapsed, max 28 chars).</summary>
    public static string Slug(string s)
    {
        var chars = s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug[..Math.Min(28, slug.Length)].TrimEnd('-');
    }

    /// <summary>Extract the shell command from a Bash/shell tool-use event's input JSON, else null.</summary>
    public static string? ExtractCommand(Events.ToolUseStarted u)
    {
        if (u.Name is not ("Bash" or "shell")) return null;
        try
        {
            using var doc = JsonDocument.Parse(u.InputJson);
            return doc.RootElement.TryGetProperty("command", out var c) ? c.GetString() : null;
        }
        catch { return null; }
    }

    // ----- repo + translator construction -----

    /// <summary>Walk up from <see cref="AppContext.BaseDirectory"/> to the first ancestor with a <c>.git</c> dir.</summary>
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")))
                return d.FullName;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>Build an <see cref="OllamaTranslator"/> from endpoint/model/lang parts (with sensible defaults when blank).</summary>
    public static OllamaTranslator CreateTranslator(string endpoint, string model, string sourceLang = "Korean", string targetLang = "English") =>
        new(new OllamaOptions
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint.Trim(),
            Model = string.IsNullOrWhiteSpace(model) ? "exaone3.5:7.8b" : model.Trim(),
            SourceLanguage = string.IsNullOrWhiteSpace(sourceLang) ? "Korean" : sourceLang.Trim(),
            TargetLanguage = string.IsNullOrWhiteSpace(targetLang) ? "English" : targetLang.Trim(),
        });

    // ----- stderr + rate-limit predicates -----

    /// <summary>Engines' harmless stderr chatter (info/warnings) that must not surface as an error block.</summary>
    public static bool IsBenignStderr(string m) =>
        m.Contains("Reading additional input")
        || m.Contains("YOLO mode is enabled")
        || m.Contains("256-color support")
        || m.Contains("Ripgrep is not available")
        || m.Contains("Retrying after")
        || m.Contains("AttachConsole failed")
        || m.Contains("node-pty") || m.Contains("conpty_console_list")
        || m.Contains("node:internal/") || m.StartsWith("Node.js v")
        || m.TrimStart().StartsWith("at ") || m.Trim() == "^"
        || m.Contains("var consoleProcessList")
        || m.Contains("UV_HANDLE_CLOSING");

    /// <summary>Does the message read like a rate-limit / quota exhaustion?</summary>
    public static bool LooksRateLimited(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        var m = msg.ToLowerInvariant();
        return m.Contains("rate limit") || m.Contains("rate_limit") || m.Contains("ratelimit")
            || m.Contains("usage limit") || m.Contains("quota") || m.Contains("429")
            || m.Contains("too many requests") || m.Contains("limit reached") || m.Contains("limit exceeded");
    }

    // ----- usage-text parsing -----

    /// <summary>Parse "session ... N% used" / "week ... N% used" out of an assistant usage message.</summary>
    public static (double session, double week) ParseUsageText(string text)
    {
        double s = -1, w = -1;
        var ms = Regex.Match(text, @"session[^0-9]*(\d+)\s*%\s*used", RegexOptions.IgnoreCase);
        if (ms.Success) s = int.Parse(ms.Groups[1].Value) / 100.0;
        var mw = Regex.Match(text, @"week[^0-9]*(\d+)\s*%\s*used", RegexOptions.IgnoreCase);
        if (mw.Success) w = int.Parse(mw.Groups[1].Value) / 100.0;
        return (s, w);
    }

    // ----- policy / auth mapping -----

    /// <summary>Map an approval-policy string to (requireApproval, sandbox).</summary>
    public static (bool requireApproval, Agents.SandboxMode sandbox) PolicyToSession(string policy) => policy switch
    {
        "ask" => (true, Agents.SandboxMode.ReadOnly),
        "safe" => (false, Agents.SandboxMode.WorkspaceWrite),
        _ => (false, Agents.SandboxMode.DangerFullAccess),
    };

    /// <summary>The API-key env-var name an engine consumes, or null if it has no API surface.</summary>
    public static string? ApiEnvVar(string id) => id switch
    {
        "cc" => "ANTHROPIC_API_KEY",
        "gx" => "OPENAI_API_KEY",
        "agy" => "GEMINI_API_KEY",
        _ => null,
    };

    /// <summary>Trim whitespace and surrounding double-quotes from a path/config value.</summary>
    public static string Clean(string value) => value.Trim().Trim('"');

    /// <summary>Normalize a translation language id against the allowed set, or return the fallback.</summary>
    public static string NormalizeTranslationLang(string? value, string fallback, IEnumerable<string> availableLanguageIds)
    {
        var v = (value ?? "").Trim();
        return availableLanguageIds.FirstOrDefault(id => string.Equals(id, v, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    // ----- process / path resolution -----

    /// <summary>Windows arg quoting: wrap in double quotes when it contains whitespace, escaping embedded quotes.</summary>
    public static string Quote(string s) =>
        s.Contains(' ') || s.Contains('\t') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;

    /// <summary>Find a bare command on PATH (first hit), or null. Tries .exe/.cmd/.bat too on Windows.</summary>
    public static string? TryResolveOnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in new[] { "", ".exe", ".cmd", ".bat" })
                try
                {
                    var full = Path.Combine(dir.Trim(), name + ext);
                    if (File.Exists(full)) return full;
                }
                catch { }
        }
        return null;
    }

    /// <summary>Locate the VS Code <c>code.cmd</c> CLI (known install dirs, then PATH), or null.</summary>
    public static string? FindVsCodeCli()
    {
        foreach (var dir in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "bin"),
        })
        {
            var cmd = Path.Combine(dir, "code.cmd");
            if (File.Exists(cmd)) return cmd;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var p = Path.Combine(dir.Trim(), "code.cmd");
            try { if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }
}
