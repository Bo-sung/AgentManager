using System.Windows;
using System.Windows.Media;

namespace AgentManager.Theme;

/// <summary>다크/라이트 팔레트를 라이브로 적용한다. Colors.{Dark,Light}.xaml의 브러시 Color를
/// 현재 리소스의 같은 키 브러시에 in-place로 복사 → StaticResource 소비자도 같은 인스턴스라 즉시 갱신.
/// (강조색은 <see cref="AccentPalette"/>가 따로 관리하므로, 적용 후 사용자의 accent를 재적용해야 한다.)</summary>
public static class ThemePalette
{
    public static string Normalize(string? name) => name == "light" ? "light" : "dark";

    public static void Apply(string theme)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;

        var src = new Uri(
            Normalize(theme) == "light" ? "Theme/Colors.Light.xaml" : "Theme/Colors.Dark.xaml",
            UriKind.Relative);
        var palette = new ResourceDictionary { Source = src };

        foreach (var key in palette.Keys)
        {
            switch (palette[key])
            {
                case SolidColorBrush sb:
                    if (res[key] is SolidColorBrush live && !live.IsFrozen) live.Color = sb.Color;
                    else res[key] = new SolidColorBrush(sb.Color);
                    break;
                case Color c: // Color는 struct — 교체(DynamicResource 소비자 갱신; 본 앱에선 Bg0Color뿐)
                    res[key] = c;
                    break;
            }
        }
    }
}
