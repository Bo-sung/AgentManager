using System.IO;
using AgentManager.Core.Agents;

namespace AgentManager.ViewModels;

public sealed record EngineDef(string Id, string Badge, string Name, string Cli, string[] Models, string Desc, bool Enabled);

/// <summary>Engine catalog + adapter/executable resolution.</summary>
public static class EngineRegistry
{
    public static readonly EngineDef[] All =
    [
        new("cc", "CC", "Claude Code",     "claude",      ["sonnet", "opus", "haiku"],        "anthropic · cli", true),
        new("gx", "GX", "GPT / Codex",     "codex",       ["gpt-5.5", "gpt-5.4", "gpt-5.4-mini"], "openai · cli", true),
        new("ag", "AG", "Antigravity CLI", "antigravity", ["gemini-3-flash", "gemini-3-pro"], "google · cli",    false),
    ];

    public static EngineDef Get(string id) => Array.Find(All, e => e.Id == id) ?? All[0];

    /// <summary>requireApproval인 codex는 app-server 경로(Stage 2 승인 게이트), 아니면 기존 exec --json.</summary>
    public static IAgentAdapter? CreateAdapter(string id, bool requireApproval = false) => id switch
    {
        "cc" => new ClaudeAdapter(),
        "gx" => requireApproval ? new CodexAppServerAdapter() : new CodexAdapter(),
        _ => null, // antigravity not wired yet
    };

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ResolveExe(string id, string? claudePath = null, string? codexPath = null) => id switch
    {
        "cc" => ResolveOverride(claudePath) ?? ResolveClaude(),
        "gx" => ResolveOverride(codexPath) ?? ResolveCodex(),
        _ => null,
    };

    private static string? ResolveOverride(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.Trim().Trim('"');
        return File.Exists(trimmed) ? trimmed : trimmed;
    }

    private static string ResolveClaude()
    {
        var p = Path.Combine(Home, ".local", "bin", "claude.exe");
        return File.Exists(p) ? p : "claude"; // fall back to PATH
    }

    private static string? ResolveCodex()
    {
        // codex CLI ships inside the openai.chatgpt VS Code extension (versioned folder).
        var extRoot = Path.Combine(Home, ".vscode", "extensions");
        if (Directory.Exists(extRoot))
            foreach (var dir in Directory.EnumerateDirectories(extRoot, "openai.chatgpt-*"))
            {
                var c = Path.Combine(dir, "bin", "windows-x86_64", "codex.exe");
                if (File.Exists(c)) return c;
            }
        return null;
    }
}
