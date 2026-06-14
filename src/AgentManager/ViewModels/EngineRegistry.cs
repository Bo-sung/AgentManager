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
        // agy를 Google 계열 기본으로 ag보다 앞에 배치 (Gemini CLI는 곧 종료 예정).
        // agy: TTY 전용 → ConPTY 구동, 텍스트 전용 v1. default = --model 생략.
        // 슬러그 형식 실측 확인: `agy -p "Say OK" --model gemini-3.5-flash` → OK (2026-06-13)
        new("agy", "AGY", "Antigravity (agy)", "agy",
            ["default", "gemini-3.5-flash", "gemini-3.1-pro", "claude-sonnet-4-6", "claude-opus-4-6", "gpt-oss-120b"],
            "google · pty", true),
        // 실측 모델 id (PHASE0_ANTIGRAVITY_GEMINI_KO). stream-json 풀 이벤트 경로
        new("ag", "AG", "Gemini CLI", "gemini", ["gemini-3-flash-preview", "gemini-3-pro-preview", "gemini-2.5-flash"], "google · cli", true),
    ];

    public static EngineDef Get(string id) => Array.Find(All, e => e.Id == id) ?? All[0];

    /// <summary>requireApproval인 codex는 app-server 경로(Stage 2 승인 게이트), 아니면 기존 exec --json.</summary>
    public static IAgentAdapter? CreateAdapter(string id, bool requireApproval = false) => id switch
    {
        "cc" => new ClaudeAdapter(),
        "gx" => requireApproval ? new CodexAppServerAdapter() : new CodexAdapter(),
        "ag" => new AntigravityAdapter(),
        "agy" => new AgyAdapter(),
        _ => null,
    };

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ResolveExe(string id, string? claudePath = null, string? codexPath = null) => id switch
    {
        "cc" => ResolveOverride(claudePath) ?? ResolveClaude(),
        "gx" => ResolveOverride(codexPath) ?? ResolveCodex(),
        "ag" => ResolveAntigravity(),
        "agy" => ResolveAgy(),
        _ => null,
    };

    private static string? ResolveAgy()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        return File.Exists(p) ? p : null;
    }

    /// <summary>ag = gemini CLI 고정 (신형 antigravity CLI는 agy 엔진이 별도로 담당 — TTY 전용이라 혼용 금지).</summary>
    private static string? ResolveAntigravity()
    {
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "gemini.cmd");
        if (File.Exists(npm)) return npm;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var p = Path.Combine(dir.Trim(), "gemini.cmd"); if (File.Exists(p)) return p; } catch { }
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
