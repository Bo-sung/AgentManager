using System.IO;
using AgentManager.Core.Agents;

namespace AgentManager.ViewModels;

public sealed record EngineDef(string Id, string Badge, string Name, string Cli, string[] Models, string Desc, bool Enabled);

/// <summary>Engine catalog + adapter/executable resolution.</summary>
public static class EngineRegistry
{
    public static readonly EngineDef[] All =
    [
        // 버전 명시 풀네임 (claude --model은 별칭/풀네임 모두 허용 — 실측; sonnet[1m] = 1M 컨텍스트 별칭)
        new("cc", "CC", "Claude Code",     "claude",      ["claude-sonnet-4-6", "claude-opus-4-8", "claude-haiku-4-5", "sonnet[1m]"], "anthropic · cli", true),
        new("gx", "GX", "GPT / Codex",     "codex",       ["gpt-5.5", "gpt-5.4", "gpt-5.4-mini"], "openai · cli", true),
        // 실측 모델 id (PHASE0_ANTIGRAVITY_GEMINI_KO). 전환(6/18) 전에는 gemini CLI로 동작
        new("ag", "AG", "Antigravity / Gemini", "antigravity", ["gemini-3-flash-preview", "gemini-3-pro-preview", "gemini-2.5-flash"], "google · cli", true),
    ];

    public static EngineDef Get(string id) => Array.Find(All, e => e.Id == id) ?? All[0];

    /// <summary>requireApproval인 codex는 app-server 경로(Stage 2 승인 게이트), 아니면 기존 exec --json.</summary>
    public static IAgentAdapter? CreateAdapter(string id, bool requireApproval = false) => id switch
    {
        "cc" => new ClaudeAdapter(),
        "gx" => requireApproval ? new CodexAppServerAdapter() : new CodexAdapter(),
        "ag" => new AntigravityAdapter(),
        _ => null,
    };

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ResolveExe(string id, string? claudePath = null, string? codexPath = null) => id switch
    {
        "cc" => ResolveOverride(claudePath) ?? ResolveClaude(),
        "gx" => ResolveOverride(codexPath) ?? ResolveCodex(),
        "ag" => ResolveAntigravity(),
        _ => null,
    };

    /// <summary>전환(6/18) 후 antigravity 우선, 그 전엔 gemini(npm .cmd 셸) 폴백.</summary>
    private static string? ResolveAntigravity()
    {
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        foreach (var name in new[] { "antigravity.cmd", "antigravity.exe", "gemini.cmd" })
        {
            var p = Path.Combine(npm, name);
            if (File.Exists(p)) return p;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in new[] { "antigravity.cmd", "antigravity.exe", "gemini.cmd" })
            {
                try { var p = Path.Combine(dir.Trim(), name); if (File.Exists(p)) return p; } catch { }
            }
        return null;
    }

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
