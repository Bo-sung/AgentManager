using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AgentManager.Core.Agents;

namespace AgentManager.ViewModels;

public sealed record EngineDef(string Id, string Badge, string Name, string Cli, string[] Models, string Desc, bool Enabled, string InstallUrl = "");

/// <summary>`pi --list-models` 결과 — provider/id 모델 목록 + 인증된(=모델이 보이는) provider 집합.</summary>
public sealed record PiCatalog(IReadOnlyList<string> Models, IReadOnlyList<string> Providers);

/// <summary>New Agent 피커 항목: 엔진 정의 + 설치 여부. Id/Name/Desc/Badge/InstallUrl을 위임 노출해
/// 기존 템플릿·아이콘(EngineIconByDef는 {Binding Id}) 바인딩과 그대로 호환된다.</summary>
public sealed class EngineOptionVm(EngineDef def, bool isInstalled, bool isLimited = false, bool willUseApi = false)
{
    public EngineDef Def { get; } = def;
    public bool IsInstalled { get; } = isInstalled;
    public bool IsLimited { get; } = isLimited;     // 사용량 한도 소진
    public bool WillUseApi { get; } = willUseApi;    // 소진이어도 API 자동전환으로 사용 가능

    /// <summary>선택(실행) 가능 — 설치됨 + (한도 OK 또는 API 자동전환).</summary>
    public bool IsAvailable => IsInstalled && (!IsLimited || WillUseApi);
    public bool Dimmed => !IsAvailable;
    public bool ShowInstallBadge => !IsInstalled;
    public bool ShowLimitBadge => IsInstalled && IsLimited && !WillUseApi;  // 한도 초과(회색)
    public bool ShowApiBadge => IsInstalled && IsLimited && WillUseApi;     // API 전환(사용 가능)

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
        // pi(pi.dev): 멀티 provider harness. node dist/cli.js --mode rpc(RPC). 모델="provider/id"(~/.pi 기본값이면 "default").
        // 모델 목록은 사용자 ~/.pi provider 설정에 따라 달라짐(`pi --list-models`가 정답). 아래는 흔한 후보 — default는 ~/.pi 기본값 사용.
        new("pi", "PI", "Pi", "pi",
            ["default", "zai/glm-4.7", "zai/glm-5.1", "zai/glm-5-turbo", "anthropic/claude-opus-4-7", "google/gemini-3.1-pro"],
            "pi.dev · multi-provider", true, "https://pi.dev"),
    ];

    public static EngineDef Get(string id) => Array.Find(All, e => e.Id == id) ?? All[0];

    /// <summary>requireApproval인 codex는 app-server 경로(Stage 2 승인 게이트), 아니면 기존 exec --json.</summary>
    public static IAgentAdapter? CreateAdapter(string id, bool requireApproval = false) => id switch
    {
        "cc" => new ClaudeAdapter(),
        "gx" => requireApproval ? new CodexAppServerAdapter() : new CodexAdapter(),
        "agy" => new AgyAdapter(),
        "pi" => new PiAdapter(),
        _ => null,
    };

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? ResolveExe(string id, string? claudePath = null, string? codexPath = null, string? agyPath = null, string? piPath = null) => id switch
    {
        "cc" => ResolveOverride(claudePath) ?? ResolveClaude(),
        "gx" => ResolveOverride(codexPath) ?? ResolveCodex(),
        "agy" => ResolveOverride(agyPath) ?? ResolveAgy(),
        "pi" => ResolveOverride(piPath) ?? ResolvePi(),
        _ => null,
    };

    /// <summary>엔진이 실제 사용 가능한가 — 수동 경로/오토 탐지로 실파일이 잡히거나, bare 명령(cc 폴백 "claude")이 PATH에 있으면 true.</summary>
    public static bool IsInstalled(string id, string? claudePath = null, string? codexPath = null, string? agyPath = null, string? piPath = null)
    {
        var exe = ResolveExe(id, claudePath, codexPath, agyPath, piPath);
        if (exe is null) return false;
        if (File.Exists(exe)) return true;
        return OnPath(exe); // bare 명령(예: cc의 "claude") → PATH 탐색
    }

    /// <summary>설치된 ollama 실행파일 경로(PATH 또는 기본 설치 위치). 미설치면 null — '실행' 버튼·상태표시용.</summary>
    public static string? OllamaExe()
    {
        var known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
        if (File.Exists(known)) return known;
        if (OnPath("ollama")) return "ollama";
        return null;
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
        "pi" => ResolvePi(),
        _ => null,
    };
    private static string? RealFile(string? p) => p is not null && File.Exists(p) ? p : null;

    private static string? ResolveAgy()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        return File.Exists(p) ? p : null;
    }

    /// <summary>pi(pi.dev)의 dist/cli.js — npm 전역 설치 경로. pi는 node 스크립트라 PiAdapter가 node로 구동한다.
    /// (별도로 node가 PATH에 있어야 함 — pi 설치 전제.)</summary>
    private static string? ResolvePi()
    {
        var p = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), // %APPDATA%
            "npm", "node_modules", "@earendil-works", "pi-coding-agent", "dist", "cli.js");
        return File.Exists(p) ? p : null;
    }

    /// <summary>`pi --list-models`로 사용 가능한 모델/인증 provider를 조회한다.
    /// pi는 인증된 provider의 모델만 보여주므로, 나오는 provider 집합 = 연동된 계정.
    /// (표는 stderr로 출력됨 — 실측. 컬럼: provider model context max-out thinking images.)</summary>
    public static async Task<PiCatalog> QueryPiCatalogAsync(string? piPath = null)
    {
        var cli = ResolveOverride(piPath) ?? ResolvePi();
        if (cli is null) return new PiCatalog([], []);
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(cli);
        psi.ArgumentList.Add("--list-models");
        var models = new List<string>();
        var providers = new List<string>();
        try
        {
            using var p = Process.Start(psi)!;
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            await Task.WhenAll(so, se);
            await p.WaitForExitAsync();
            var text = (se.Result?.Length ?? 0) >= (so.Result?.Length ?? 0) ? se.Result : so.Result;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0] == "provider") continue; // 헤더/빈 줄 스킵
                models.Add(parts[0] + "/" + parts[1]);
                if (!providers.Contains(parts[0])) providers.Add(parts[0]);
            }
        }
        catch { /* 미설치/실패 → 빈 카탈로그 */ }
        return new PiCatalog(models, providers);
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
