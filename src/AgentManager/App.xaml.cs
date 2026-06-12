using System.Windows;

namespace AgentManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 테마는 재시작 시 적용: 색 팔레트(Colors.*)를 Theme.xaml보다 먼저 머지해야
        // Theme.xaml의 StaticResource들이 해당 팔레트를 캡처한다 → 전체 재머지.
        string theme;
        try { theme = Persistence.AppStateStore.Load()?.Settings.Theme ?? "dark"; }
        catch { theme = "dark"; }
        if (theme == "light")
        {
            var dicts = Resources.MergedDictionaries;
            dicts.Clear();
            dicts.Add(new ResourceDictionary { Source = new Uri("Theme/Colors.Light.xaml", UriKind.Relative) });
            dicts.Add(new ResourceDictionary { Source = new Uri("Theme/Theme.xaml", UriKind.Relative) });
            dicts.Add(new ResourceDictionary { Source = new Uri("Theme/Icons.xaml", UriKind.Relative) });
        }
        base.OnStartup(e);
    }
}
