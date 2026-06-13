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
    ];

    public static string Normalize(string? name) =>
        Array.Exists(All, p => p.Name == name) ? name! : "ember";

    public static void Apply(string name)
    {
        var p = Array.Find(All, x => x.Name == name) ?? All[0];
        SetBrush("Accent", p.Accent);
        SetBrush("AccentBright", p.Bright);
        SetBrush("Run", p.Accent);
        SetBrush("AccentDim", WithAlpha(p.Accent, 0x24));   // ~0.14
        SetBrush("AccentLine", WithAlpha(p.Accent, 0x66));  // ~0.40
    }

    private static void SetBrush(string key, Color c)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        if (res[key] is SolidColorBrush b && !b.IsFrozen)
            b.Color = c;
        else
            res[key] = new SolidColorBrush(c);
    }

    private static Color Hex(string s) => (Color)ColorConverter.ConvertFromString(s)!;
    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
