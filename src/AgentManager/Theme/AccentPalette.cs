using System.Windows;
using System.Windows.Media;

namespace AgentManager.Theme;

/// <summary>강조색 프리셋. 브러시 인스턴스의 Color를 직접 바꿔 라이브 적용하고
/// (StaticResource 소비자도 같은 브러시 참조라 즉시 갱신), frozen이면 리소스 엔트리를 교체한다.</summary>
public static class AccentPalette
{
    public sealed record Preset(string Name, Color Accent, Color Bright);

    public static readonly Preset[] All =
    [
        new("ember",  Hex("#FFFF5A2C"), Hex("#FFFF7048")),
        new("amber",  Hex("#FFF5B531"), Hex("#FFFFC94D")),
        new("teal",   Hex("#FF2BD4C4"), Hex("#FF4FE6D7")),
        new("azure",  Hex("#FF5B8CFF"), Hex("#FF7BA6FF")),
        new("violet", Hex("#FFA87BFF"), Hex("#FFC0A0FF")),
        new("coral",  Hex("#FFD97757"), Hex("#FFE0896B")),  // Claude
        new("green",  Hex("#FF10A37F"), Hex("#FF1FBF96")),  // OpenAI/Codex
        new("cobalt", Hex("#FF4285F4"), Hex("#FF6BA1FF")),  // Google/Antigravity
    ];

    /// <summary>프리셋 이름 또는 유효한 hex(#AARRGGBB/#RRGGBB)면 그대로, 아니면 ember.</summary>
    public static string Normalize(string? name) =>
        name is not null && (Array.Exists(All, p => p.Name == name) || IsHex(name)) ? name : "ember";

    /// <summary>프리셋 이름 또는 커스텀 hex를 받아 강조색 토큰을 적용한다.</summary>
    public static void Apply(string name)
    {
        var (accent, bright) = Resolve(name);
        SetBrush("Accent", accent);
        SetBrush("AccentBright", bright);
        SetBrush("Run", accent);
        SetBrush("AccentDim", WithAlpha(accent, 0x24));   // ~0.14
        SetBrush("AccentLine", WithAlpha(accent, 0x66));  // ~0.40
    }

    private static (Color accent, Color bright) Resolve(string? name)
    {
        if (name is not null && Array.Find(All, x => x.Name == name) is { } p) return (p.Accent, p.Bright);
        if (TryHex(name, out var c)) return (c, Lighten(c, 0.16));
        return (All[0].Accent, All[0].Bright);
    }

    /// <summary>유효한 hex 색 문자열인지(#으로 시작 + 파싱 가능).</summary>
    public static bool IsHex(string? s) => TryHex(s, out _);

    private static bool TryHex(string? s, out Color c)
    {
        c = default;
        var t = s?.Trim();
        if (string.IsNullOrEmpty(t) || t[0] != '#') return false;
        try { var v = (Color)ColorConverter.ConvertFromString(t)!; c = v.A == 0 ? Color.FromArgb(0xFF, v.R, v.G, v.B) : v; return true; }
        catch { return false; }
    }

    private static Color Lighten(Color c, double f)
    {
        byte L(byte v) => (byte)Math.Clamp(v + (255 - v) * f, 0, 255);
        return Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B));
    }

    // 색 토큰은 DynamicResource로 참조되므로 엔트리를 덮어쓰면 소비자가 즉시 재해석한다.
    private static void SetBrush(string key, Color c)
    {
        if (Application.Current?.Resources is { } res) res[key] = new SolidColorBrush(c);
    }

    private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s)!;
    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
