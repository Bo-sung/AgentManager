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
        // Review pane 펼침/접힘 .22s 슬라이드 (GridLength는 XAML 애니메이션 불가 → code-behind).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModels.AppViewModel.ReviewPaneWidth)) AnimateReviewPane();
        };
        Loaded += (_, _) => AnimateReviewPane();
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
        // UI 줌 단축키 (Ctrl+0 리셋 · Ctrl++/Ctrl+- 인/아웃, 넘패드 포함)
        InputBindings.Add(new KeyBinding(_vm.ZoomResetCommand, Key.D0, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ZoomResetCommand, Key.NumPad0, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ZoomInCommand, Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ZoomInCommand, Key.Add, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ZoomOutCommand, Key.OemMinus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(_vm.ZoomOutCommand, Key.Subtract, ModifierKeys.Control));
        PreviewMouseWheel += OnPreviewMouseWheelZoom;
        RestoreWindowPlacement();
        Closing += (_, _) =>
        {
            SaveWindowPlacement();
            // 종료 전 마지막 상태를 동기로 강제 저장 — 대기 중인 디바운스 저장을 잃지 않도록(데이터 손실 방지).
            // Dispose()가 진행 중 세션을 취소하기 전에 호출해 현재 트랜스크립트를 그대로 캡처한다.
            _vm.FlushStateNow(synchronousWrite: true);
            _vm.Dispose();
        };
    }

    // Ctrl+휠 = UI 줌 (크롬/파폭식). Window 터널링이라 트랜스크립트 스크롤 중계보다 먼저 발화.
    private void OnPreviewMouseWheelZoom(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Delta != 0)
        {
            _vm.ZoomBy(System.Math.Sign(e.Delta));
            e.Handled = true;
        }
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

    /// <summary>Review pane 컬럼 폭을 현재값에서 목표(0/420)로 0.22s EaseOut 애니메이션.</summary>
    private void AnimateReviewPane()
    {
        var anim = new Controls.GridLengthAnimation
        {
            To = _vm.ReviewPaneWidth,
            Duration = new Duration(TimeSpan.FromSeconds(0.22)),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            },
        };
        ReviewCol.BeginAnimation(ColumnDefinition.WidthProperty, anim);
    }

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // 뷰별 핸들러는 Views/*.xaml.cs로 이동:
    //   Settings  -> SettingsView (SettingsToc/ApprovalPolicy/AccentSwatch/DensitySeg/EngineSignIn/AuthMode/AddExtraPath)
    //   History   -> HistoryView (HistoryRow_Click)
    //   Orchestr. -> OrchestratorView (OrchCardDiff_Click)
    //   Session   -> SessionView (트랜스크립트 스크롤·내보내기, 컴포저 전송/서제스천/이미지/받아쓰기/모델·노력 선택)

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
            if (parts.Length >= 4
                && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var t)
                && double.TryParse(parts[2], out var w) && double.TryParse(parts[3], out var h)
                && w > 200 && h > 200)
            {
                // 저장된 건 항상 '복원(normal) 크기' — 최대화 상태에서도 RestoreBounds를 기록한다.
                Left = l; Top = t; Width = w; Height = h;
                if (parts.Length >= 5 && parts[4] == "max") WindowState = WindowState.Maximized;
            }
        }
        catch { }
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
            // 최대화/최소화 상태에서는 RestoreBounds(복원 크기)를 저장해야 다음 실행 때 거대한 창이 안 뜬다.
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (r.IsEmpty || r.Width < 200 || r.Height < 200) r = new Rect(Left, Top, Width, Height);
            var state = WindowState == WindowState.Maximized ? "max" : "normal";
            System.IO.File.WriteAllText(WindowStatePath,
                string.Join(",", r.Left, r.Top, r.Width, r.Height, state));
        }
        catch { }
    }
}
