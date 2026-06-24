using System.IO;
using AgentManager.Core.Agents;

namespace AgentManager.ViewModels;

public sealed record EngineDef(string Id, string Badge, string Name, string Cli, string[] Models, string Desc, bool Enabled, string InstallUrl = "");

/// <summary>New Agent 피커 항목: 엔진 정의 + 설치 여부. Id/Name/Desc/Badge/InstallUrl을 위임 노출해
/// 기존 템플릿·아이콘(EngineIconByDef는 {Binding Id}) 바인딩과 그대로 호환된다.</summary>
public sealed class EngineOptionVm(EngineDef def, bool isInstalled)
{
    public EngineDef Def { get; } = def;
    public bool IsInstalled { get; } = isInstalled;
    public string Id => Def.Id;
    public string Name => Def.Name;
    public string Desc => Def.Desc;
    public string Badge => Def.Badge;
    public string InstallUrl => Def.InstallUrl;
}

/// <summary>Engine catalog + adapter/executable resolution.</summary>
public static class EngineRegistry
{
    public static readonly EngineDef[] All =
    [
        // 버전 명시 풀네임 (claude --model은 별칭/풀네임 모두 허용 — 실측; sonnet[1m] = 1M 컨텍스트 별칭)
        new("cc", "CC", "Claude Code",     "claude",      ["claude-sonnet-4-6", "claude-opus-4-8", "claude-haiku-4-5", "sonnet[1m]"], "anthropic · cli", true, "https://docs.claude.com/en/docs/claude-code/overview"),
        new("gx", "GX", "Codex",           "codex",       ["gpt-5.5", "gpt-5.4", "gpt-5.4-mini"], "openai · cli", true, "https://github.com/openai/codex"),
        // Google 계열은 agy(Antigravity)로 일원화 — 구형 Gemini CLI는 제거됨.
        // agy: TTY 전용 → ConPTY 구동, 텍스트 전용 v1. default = --model 생략.
        // 슬러그 형식 실측 확인: `agy -p "Say OK" --model gemini-3.5-flash` → OK (2026-06-13)
        new("agy", "AG", "Antigravity",    "agy",
            ["default", "gemini-3.5-flash", "gemini-3.1-pro", "claude-sonnet-4-6", "claude-opus-4-6", "gpt-oss-120b"],
            "google · pty", true, "https://antigravity.google"),
    ];

    public static EngineDef Get(string id) => Array.Find(All, e => e.Id == id) ?? All[0];

    /// <summary>requireApproval인 codex는 app-server 경로(Stage 2 승인 게이트), 아니면 기존 exec --json.</summary>
    public static IAgentAdapter? CreateAdapter(string id, bool requireApproval = false) => id switch
    {
        "cc" => new ClaudeAdapter(),
        "gx" => requireApproval ? new CodexAppServerAdapter() : new CodexAdapter(),
        "agy" => new AgyAdapter(),
        _ => null,
    };

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ResolveExe(string id, string? claudePath = null, string? codexPath = null, string? agyPath = null) => id switch
    {
        "cc" => ResolveOverride(claudePath) ?? ResolveClaude(),
        "gx" => ResolveOverride(codexPath) ?? ResolveCodex(),
        "agy" => ResolveOverride(agyPath) ?? ResolveAgy(),
        _ => null,
    };

    /// <summary>엔진이 실제 사용 가능한가 — 수동 경로/오토 탐지로 실파일이 잡히거나, bare 명령(cc 폴백 "claude")이 PATH에 있으면 true.</summary>
    public static bool IsInstalled(string id, string? claudePath = null, string? codexPath = null, string? agyPath = null)
    {
        var exe = ResolveExe(id, claudePath, codexPath, agyPath);
        if (exe is null) return false;
        if (File.Exists(exe)) return true;
        return OnPath(exe); // bare 명령(예: cc의 "claude") → PATH 탐색
    }

    private static bool OnPath(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        string[] exts = ["", ".exe", ".cmd", ".bat"];
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in exts)
                try { if (File.Exists(Path.Combine(dir, command + ext))) return true; } catch { }
        }
        return false;
    }

    /// <summary>오토 탐지만 수행(수동 경로 무시) — 설정의 '탐지' 버튼용. 실제 존재하는 exe만 반환, 없으면 null.</summary>
    public static string? DetectExe(string id) => id switch
    {
        "cc" => RealFile(ResolveClaude()),   // ResolveClaude는 미발견 시 "claude"(PATH 폴백)를 주므로 실제 파일만 거른다
        "gx" => ResolveCodex(),
        "agy" => ResolveAgy(),
        _ => null,
    };
    private static string? RealFile(string? p) => p is not null && File.Exists(p) ? p : null;

    private static string? ResolveAgy()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        return File.Exists(p) ? p : null;
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
        // 1순위: 독립 설치(npm 전역 @openai/codex)의 네이티브 바이너리.
        // 2순위: openai.chatgpt VS Code 확장에 번들된 codex(여러 버전이면 최신).
        => ResolveCodexNpm() ?? ResolveCodexVscode();

    /// <summary>npm 전역 @openai/codex 패키지의 플랫폼 네이티브 codex.exe.
    /// (PATH의 codex는 node 셸 심(.cmd/.ps1)이라 실제 exe를 vendor 트리에서 찾는다.)</summary>
    private static string? ResolveCodexNpm()
    {
        var pkg = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), // %APPDATA%
            "npm", "node_modules", "@openai", "codex",
            "node_modules", "@openai", "codex-win32-x64", "vendor");
        if (!Directory.Exists(pkg)) return null;
        foreach (var exe in Directory.EnumerateFiles(pkg, "codex.exe", SearchOption.AllDirectories))
            return exe; // .../bin/codex.exe
        return null;
    }

    /// <summary>VS Code 확장(openai.chatgpt-*) 번들 codex — 여러 버전 설치 시 가장 최근 빌드.</summary>
    private static string? ResolveCodexVscode()
    {
        var extRoot = Path.Combine(Home, ".vscode", "extensions");
        if (!Directory.Exists(extRoot)) return null;
        string? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var dir in Directory.EnumerateDirectories(extRoot, "openai.chatgpt-*"))
        {
            var c = Path.Combine(dir, "bin", "windows-x86_64", "codex.exe");
            if (!File.Exists(c)) continue;
            var t = File.GetLastWriteTimeUtc(c);
            if (t > bestTime) { bestTime = t; best = c; }
        }
        return best;
    }
}
