using System.Globalization;
using System.Windows;

namespace AgentManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 테마는 재시작 시 적용: 색 팔레트(Colors.*)를 Theme.xaml보다 먼저 머지해야
        // Theme.xaml의 StaticResource들이 해당 팔레트를 캡처한다 → 전체 재머지.
        string theme;
        string language;
        try
        {
            var settings = Persistence.AppStateStore.Load()?.Settings;
            theme = settings?.Theme ?? "dark";
            language = settings?.Language ?? "ko";
        }
        catch
        {
            theme = "dark";
            language = "ko";
        }
        // 저장된 테마/언어로 팔레트를 머지한다(색 토큰은 DynamicResource로 참조되므로 시작 시 이 팔레트가
        // 보이면 됨; 이후 전환은 ThemePalette.Apply가 엔트리를 덮어써 라이브 반영).
        var dicts = Resources.MergedDictionaries;
        dicts.Clear();
        dicts.Add(new ResourceDictionary { Source = new Uri(Theme.ThemePalette.FileFor(theme), UriKind.Relative) });
        dicts.Add(new ResourceDictionary { Source = new Uri("Theme/Theme.xaml", UriKind.Relative) });
        dicts.Add(new ResourceDictionary { Source = new Uri("Theme/Icons.xaml", UriKind.Relative) });
        dicts.Add(new ResourceDictionary { Source = new Uri(language == "en" ? "Theme/Strings.En.xaml" : "Theme/Strings.Ko.xaml", UriKind.Relative) });
        base.OnStartup(e);
    }

    public static string L(string key, params object?[] args)
    {
        var value = Current?.Resources[key] as string ?? key;
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }
}
