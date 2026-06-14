using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentManager.Persistence;

/// <summary>각 CLI가 자체 보관하는 로그인 계정(구독 인증)을 읽어 표시용 이메일을 돌려준다.
/// 토큰 값은 사용하지 않고 계정 식별 필드만 추출한다. 로그인 안 됐으면 null.
///   cc  : ~/.claude.json → oauthAccount.emailAddress
///   gx  : ~/.codex/auth.json → tokens.id_token(JWT)의 email, 없으면 OPENAI_API_KEY 모드
///   ag  : ~/.gemini/google_accounts.json → active
///   agy : 동일 Google 계정(~/.gemini) 공유</summary>
internal static class EngineAccounts
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? For(string id) => id switch
    {
        "cc" => Claude(),
        "gx" => Codex(),
        "ag" => Gemini(),
        "agy" => Gemini(), // agy는 ~/.gemini의 Google 계정을 공유
        _ => null,
    };

    private static string? Claude()
    {
        var f = Path.Combine(Home, ".claude.json");
        if (!File.Exists(f)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            if (doc.RootElement.TryGetProperty("oauthAccount", out var acc) &&
                acc.TryGetProperty("emailAddress", out var em) &&
                em.ValueKind == JsonValueKind.String)
                return Trim(em.GetString());
        }
        catch { }
        return null;
    }

    private static string? Codex()
    {
        var f = Path.Combine(Home, ".codex", "auth.json");
        if (!File.Exists(f)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            var root = doc.RootElement;
            // ChatGPT 구독 로그인: id_token(JWT) payload의 email
            if (root.TryGetProperty("tokens", out var tk) &&
                tk.TryGetProperty("id_token", out var idt) && idt.ValueKind == JsonValueKind.String)
            {
                var email = JwtEmail(idt.GetString());
                if (!string.IsNullOrEmpty(email)) return email;
            }
            // API 키 모드
            if (root.TryGetProperty("OPENAI_API_KEY", out var k) &&
                k.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(k.GetString()))
                return "API key";
        }
        catch { }
        return null;
    }

    private static string? Gemini()
    {
        var f = Path.Combine(Home, ".gemini", "google_accounts.json");
        if (!File.Exists(f)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            if (doc.RootElement.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.String)
                return Trim(a.GetString());
        }
        catch { }
        return null;
    }

    /// <summary>JWT payload(가운데 세그먼트)만 base64url 디코드해 email 클레임 추출. 서명/검증 없음(표시 전용).</summary>
    private static string? JwtEmail(string? jwt)
    {
        if (string.IsNullOrEmpty(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String)
                return Trim(em.GetString());
        }
        catch { }
        return null;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
