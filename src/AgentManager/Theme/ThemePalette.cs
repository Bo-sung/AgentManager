using System.Windows;
using System.Windows.Media;

namespace AgentManager.Theme;

/// <summary>선택 가능한 테마 정의 (id + 표시 이름).</summary>
public sealed record ThemeDef(string Id, string Name);

/// <summary>
/// 명명된 테마 팔레트를 라이브로 적용한다. 색 토큰은 <c>DynamicResource</c>로 참조되므로,
/// Apply는 Colors.&lt;Theme&gt;.xaml의 브러시를 <see cref="Application"/>.Resources의 같은 키에
/// *덮어쓰기*만 하면 모든 소비자가 즉시 재해석한다. (BAML 브러시는 머지 시 frozen 되지만,
/// 교체 방식이라 frozen이어도 무방 — in-place 변경이 아님.)
/// 강조색은 <see cref="AccentPalette"/>가 따로 관리하므로 적용 후 사용자 accent를 재적용한다.
/// </summary>
public static class ThemePalette
{
    /// <summary>선택 가능한 테마. 다크/라이트 + IDE 스타일 프리셋.</summary>
    public static readonly ThemeDef[] All =
    [
        new("dark",    "Dark"),
        new("light",   "Light"),
        new("gray",    "Gray"),
        new("vs",      "Visual Studio"),
        new("vscode",  "VS Code"),
        new("monokai", "Monokai"),
        new("nord",    "Nord"),
        new("claude",          "Claude"),
        new("claudedark",      "Claude Dark"),
        new("codex",           "Codex"),
        new("codexlight",      "Codex Light"),
        new("antigravity",     "Antigravity"),
        new("antigravitylight","Antigravity Light"),
    ];

    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dark"]    = "Theme/Colors.Dark.xaml",
        ["light"]   = "Theme/Colors.Light.xaml",
        ["gray"]    = "Theme/Colors.Gray.xaml",
        ["vs"]      = "Theme/Colors.Vs.xaml",
        ["vscode"]  = "Theme/Colors.VsCode.xaml",
        ["monokai"] = "Theme/Colors.Monokai.xaml",
        ["nord"]    = "Theme/Colors.Nord.xaml",
        ["claude"]          = "Theme/Colors.Claude.xaml",
        ["claudedark"]      = "Theme/Colors.ClaudeDark.xaml",
        ["codex"]           = "Theme/Colors.Codex.xaml",
        ["codexlight"]      = "Theme/Colors.CodexLight.xaml",
        ["antigravity"]     = "Theme/Colors.Antigravity.xaml",
        ["antigravitylight"]= "Theme/Colors.AntigravityLight.xaml",
    };

    /// <summary>알 수 없는 이름은 "dark"로 정규화.</summary>
    public static string Normalize(string? name)
        => name is not null && Files.ContainsKey(name) ? name.ToLowerInvariant() : "dark";

    /// <summary>테마 id → 색 팔레트 리소스 경로.</summary>
    public static string FileFor(string? theme)
        => Files.TryGetValue(Normalize(theme), out var f) ? f : Files["dark"];

    /// <summary>선택 테마의 모든 색 토큰을 Application.Resources에 덮어써 라이브 적용한다.</summary>
    public static void Apply(string theme)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        var palette = new ResourceDictionary { Source = new Uri(FileFor(theme), UriKind.Relative) };
        foreach (var key in palette.Keys)
            res[key] = palette[key];
    }
}
