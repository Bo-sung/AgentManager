using System.Globalization;
using System.Windows;
using Velopack;

namespace AgentManager;

public partial class App : Application
{
    private static readonly string CrashLogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager", "crash.log");

    /// <summary>예외를 crash.log에 추가 기록(진단용). 절대 throw 안 함.</summary>
    public static void LogException(string source, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
            System.IO.File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch { }
    }

    private static int _crashing;
    /// <summary>치명적 예외: crash.log에 기록하고 오류 팝업을 띄운 뒤 프로세스를 종료한다.
    /// 중복 진입(연쇄 예외) 시 첫 호출만 팝업/종료를 수행한다.</summary>
    public static void FatalCrash(string source, Exception? ex)
    {
        if (System.Threading.Interlocked.Exchange(ref _crashing, 1) != 0) return;
        LogException(source, ex);
        try
        {
            var detail = ex is null ? "(알 수 없는 오류)" : $"{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(
                $"예기치 않은 오류로 AgentManager를 종료합니다.\n\n{detail}\n\n전체 로그:\n{CrashLogPath}",
                "AgentManager 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        Environment.Exit(1);
    }

    // 자식 CLI(codex/claude 등)가 크래시할 때 뜨는 Windows "응용 프로그램 오류" 대화상자를 억제한다.
    // 에러 모드는 자식 프로세스에 상속되므로, 예: codex app-server가 Windows에 없는 샌드박스 헬퍼
    // (false.exe)를 spawn하다 실패해도 모달 박스가 뜨지 않는다. (앱 본체는 전역 핸들러로 보호)
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack: handle install/update/uninstall hooks (the installer/updater relaunches us with special args).
        // MUST run before any real startup work; on a hook it does its job and exits, so normal launch continues below.
        VelopackApp.Build().Run();

        // Velopack launches us with the working directory set to the install folder (…\current\). If we keep it,
        // the process — and any child spawned without an explicit WorkingDirectory (notably the long-running,
        // orphan-surviving `ollama serve`) — holds …\current\ open as its cwd, which blocks the updater from
        // replacing that folder ("file in use") and makes self-update fail. Move to the stable data dir so neither
        // this process nor an inheriting child ever locks the install folder. (Our own file access uses absolute
        // paths / AppContext.BaseDirectory, so cwd is not otherwise relied upon.)
        try
        {
            var dataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentManager");
            System.IO.Directory.CreateDirectory(dataDir);
            Environment.CurrentDirectory = dataDir;
        }
        catch { /* cwd move is best-effort */ }

        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);

        // 전역 예외 가드 — 처리되지 않은 UI/AppDomain 예외(=크래시)는 오류 로그 팝업을 띄우고 종료한다.
        // Task(awaiter 없는 백그라운드) 예외는 크래시가 아니므로 기록만 하고 흡수한다.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true; // WPF 기본 크래시 처리/WER 박스 억제 — 종료는 우리가 직접
            FatalCrash("Dispatcher", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FatalCrash("AppDomain" + (args.IsTerminating ? "(terminating)" : ""), args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("UnobservedTask", args.Exception);
            args.SetObserved();
        };

        // 테마는 재시작 시 적용: 색 팔레트(Colors.*)를 Theme.xaml보다 먼저 머지해야
        // Theme.xaml의 StaticResource들이 해당 팔레트를 캡처한다 → 전체 재머지.
        string theme;
        string language;
        try
        {
            var settings = Persistence.SettingsStore.Load();
            theme = settings.Theme ?? "dark";
            language = settings.Language ?? "ko";
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
