using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentManager.ViewModels;

namespace AgentManager;

public partial class MainWindow : Window
{
    private readonly AppViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.Dialogs = new MessageBoxDialogService();
        _vm.AttentionRequested += OnAttentionRequested;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => SessionSearchBox.Focus()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Find, Key.F, ModifierKeys.Control));
        // menu-mirrored shortcuts (Agents/View)
        InputBindings.Add(new KeyBinding(_vm.NewAgentCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ShowViewCommand, Key.D1, ModifierKeys.Control) { CommandParameter = "orchestrator" });
        InputBindings.Add(new KeyBinding(_vm.ShowViewCommand, Key.D2, ModifierKeys.Control) { CommandParameter = "history" });
        InputBindings.Add(new KeyBinding(_vm.ShowViewCommand, Key.D3, ModifierKeys.Control) { CommandParameter = "scheduled" });
        InputBindings.Add(new KeyBinding(_vm.ToggleReviewCommand, Key.R, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ShowSettingsCommand, Key.OemComma, ModifierKeys.Control));
        RestoreWindowPlacement();
        Closing += (_, _) =>
        {
            SaveWindowPlacement();
            _vm.Dispose();
        };
    }

    // WindowStyle=None + WindowChrome: 기본 최대화는 창을 작업영역보다 ~리사이즈보더만큼 크게
    // 만들어 우상단 닫기/최대화/최소화 버튼의 히트영역을 화면 밖으로 밀어낸다(클릭 무반응).
    // WM_GETMINMAXINFO에서 최대화 크기/위치를 모니터 작업영역에 정확히 맞춰 overflow를 차단한다.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x2;

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                    mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                    mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                    mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
        }
        return IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT ptReserved; public POINT ptMaxSize; public POINT ptMaxPosition; public POINT ptMinTrackSize; public POINT ptMaxTrackSize; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    // ----- attention notifications (flash taskbar when unfocused; sound for approvals) -----
    private void OnAttentionRequested(string reason, ViewModels.SessionViewModel s)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (reason == "approval")
                System.Media.SystemSounds.Exclamation.Play();
            if (!IsActive)
                FlashTaskbar();
        });
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private void FlashTaskbar()
    {
        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            var fi = new FLASHWINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FLASHWINFO>(),
                hwnd = h,
                dwFlags = 0x3 | 0xC, // FLASHW_ALL | FLASHW_TIMERNOFG: flash until the window comes to foreground
                uCount = 0,
                dwTimeout = 0,
            };
            FlashWindowEx(ref fi);
        }
        catch { }
    }

    /// <summary>Edit ▸ Search sessions: 사이드바 검색창으로 포커스 이동 (Ctrl+F).</summary>
    private void FocusSearch_Click(object sender, RoutedEventArgs e) => SessionSearchBox.Focus();

    /// <summary>Agents ▸ 엔진별 새 세션: 엔진을 미리 선택한 채 New Agent 폼을 연다.</summary>
    private void NewAgentEngine_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id)
        {
            var engine = _vm.Engines.FirstOrDefault(en => en.Id == id);
            if (engine is not null) _vm.NewAgentSelectedEngine = engine;
        }
        _vm.ShowNewAgent = true;
    }

    /// <summary>타이틀바 메뉴 버튼: 좌클릭으로 ContextMenu를 드롭다운처럼 연다.</summary>
    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.ContextMenu is { } m)
        {
            m.DataContext ??= DataContext;
            m.PlacementTarget = b;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            m.IsOpen = true;
        }
    }

    private void AboutOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
    {
        _vm.ShowAbout = false;
    }

    private void AboutOverlay_CardClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void HelpDocs_Click(object sender, RoutedEventArgs e)
    {
        var docs = System.IO.Path.Combine(AppContext.BaseDirectory, "docs");
        if (!System.IO.Directory.Exists(docs))
            docs = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
        if (System.IO.Directory.Exists(docs))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = docs, UseShellExecute = true });
        else
            MessageBox.Show(this, App.L("L.DocsNotFound"), App.L("L.HelpTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void SessionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionViewModel s)
            _vm.ActiveSession = s;
    }

    private void Orchestrator_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.CurrentView = MainViewKind.Orchestrator;
    }

    private void ActivityHistory_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.CurrentView = MainViewKind.History;
    }

    private void ScheduledTasks_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.CurrentView = MainViewKind.Scheduled;
    }

    // 뷰별 핸들러는 Views/*.xaml.cs로 이동:
    //   Settings  -> SettingsView (SettingsToc/ApprovalPolicy/AccentSwatch/DensitySeg/EngineSignIn/AuthMode/AddExtraPath)
    //   History   -> HistoryView (HistoryRow_Click)
    //   Orchestr. -> OrchestratorView (OrchCardDiff_Click)
    //   Session   -> SessionView (트랜스크립트 스크롤·내보내기, 컴포저 전송/서제스천/이미지/받아쓰기/모델·노력 선택)

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectViewModel p)
            _vm.ActiveProject = p;
    }

    private void EngineOpt_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EngineDef def)
            _vm.NewAgentSelectedEngine = def;
    }

    private void ReviewChanges_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is ReviewChangeViewModel change)
            _ = _vm.SelectReviewChangeAsync(change);
    }

    private void DiffFeedbackSend_Click(object sender, RoutedEventArgs e)
    {
        DiffFeedbackBox.Text = "";
    }

    /// <summary>트랜스크립트 메시지의 복사 버튼. 이 핸들러의 DataTemplate(AgentTextBlock)은 Window.Resources에
    /// 정의되어 있어, 핸들러도 MainWindow에 남아야 한다(SessionView로 옮기면 안 됨).</summary>
    private void CopyAgentText_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AgentTextBlock b)
            try { Clipboard.SetText(b.Text); } catch { }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (_vm.ShowAbout) { _vm.ShowAbout = false; e.Handled = true; }
            else if (_vm.ShowNewAgent) { _vm.ShowNewAgent = false; e.Handled = true; }
            else if (_vm.ShowNewProject) { _vm.ShowNewProject = false; e.Handled = true; }
            else if (_vm.ShowNewSchedule) { _vm.ShowNewSchedule = false; e.Handled = true; }
        }
    }

    private static readonly string WindowStatePath =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentManager", "window.json");

    private void RestoreWindowPlacement()
    {
        try
        {
            if (!System.IO.File.Exists(WindowStatePath)) return;
            var parts = System.IO.File.ReadAllText(WindowStatePath).Split(',');
            if (parts.Length == 4
                && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var t)
                && double.TryParse(parts[2], out var w) && double.TryParse(parts[3], out var h)
                && w > 200 && h > 200)
            { Left = l; Top = t; Width = w; Height = h; }
        }
        catch { }
    }

    private void CliHistoryRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CliHistoryItemViewModel h)
            _vm.ImportCliSessionCommand.Execute(h);
    }

    private void BrowseProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = App.L("L.ProjectFolderDialogTitle"),
            InitialDirectory = System.IO.Directory.Exists(_vm.NewProjectPath?.Trim().Trim('"'))
                ? System.IO.Path.GetFullPath(_vm.NewProjectPath!.Trim().Trim('"'))
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog(this) == true)
            _vm.NewProjectPath = dlg.FolderName;
    }

    private void SaveWindowPlacement()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowStatePath)!);
            System.IO.File.WriteAllText(WindowStatePath,
                string.Join(",", Left, Top, Width, Height));
        }
        catch { }
    }
}
