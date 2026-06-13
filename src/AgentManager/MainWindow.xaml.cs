using System.Collections.Specialized;
using System.ComponentModel;
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
        _vm.PropertyChanged += Vm_PropertyChanged;
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

    /// <summary>가상화 때문에 ScrollViewer는 TranscriptList 템플릿 내부에 있다 (PART_TranscriptScroll).</summary>
    private ScrollViewer? _transcriptScrollCache;
    private ScrollViewer? TranscriptScroll
    {
        get
        {
            if (_transcriptScrollCache is null)
            {
                TranscriptList.ApplyTemplate();
                _transcriptScrollCache = TranscriptList.Template.FindName("PART_TranscriptScroll", TranscriptList) as ScrollViewer;
            }
            return _transcriptScrollCache;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppViewModel.ActiveSession) && _vm.ActiveSession is { } s)
        {
            s.Transcript.CollectionChanged -= Transcript_Changed;
            s.Transcript.CollectionChanged += Transcript_Changed;
            TranscriptScroll?.ScrollToEnd();
        }
    }

    private void Transcript_Changed(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.InvokeAsync(() =>
        {
            // auto-follow only when the user is already near the bottom; never yank them back mid-read
            if (TranscriptScroll is { } sv && sv.ScrollableHeight - sv.VerticalOffset < 80)
                sv.ScrollToEnd();
        });

    /// <summary>중앙 트랜스크립트 휠 스크롤 보장: 내부 스크롤러(마크다운 FlowDocument, 툴 출력,
    /// 읽기전용 TextBox 등)가 더 스크롤할 게 없으면 휠을 가로채 바깥 트랜스크립트를 움직인다.</summary>
    private void TranscriptScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TranscriptScroll is not { } outer) return;
        var inner = FindInnerScrollViewer(e.OriginalSource as DependencyObject, outer);
        if (inner is not null)
        {
            var canScroll = e.Delta > 0 ? inner.VerticalOffset > 0.5 : inner.VerticalOffset < inner.ScrollableHeight - 0.5;
            if (canScroll) return; // let the inner scroller consume the wheel
        }
        outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>OriginalSource에서 TranscriptScroll까지 올라가며 처음 만나는 내부 ScrollViewer를 찾는다.
    /// (FlowDocument의 Run 등 비주얼이 아닌 노드는 논리 트리로 우회)</summary>
    private static ScrollViewer? FindInnerScrollViewer(DependencyObject? node, ScrollViewer outer)
    {
        while (node is not null && !ReferenceEquals(node, outer))
        {
            if (node is ScrollViewer sv) return sv;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
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

    /// <summary>마이크: 입력창에 포커스를 주고 Windows 받아쓰기(Win+H)를 연다 — OS STT를 그대로 활용.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private void Dictate_Click(object sender, RoutedEventArgs e)
    {
        ComposerBox.Focus();
        const byte VK_LWIN = 0x5B, VK_H = 0x48;
        const uint KEYEVENTF_KEYUP = 0x2;
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_H, 0, 0, UIntPtr.Zero);
        keybd_event(VK_H, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
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

    private void HelpAbout_Click(object sender, RoutedEventArgs e)
    {
        AboutOverlay.Visibility = Visibility.Visible;
    }

    private void CloseAbout_Click(object sender, RoutedEventArgs e) => AboutOverlay.Visibility = Visibility.Collapsed;

    private void AboutOverlay_BackdropClick(object sender, MouseButtonEventArgs e)
    {
        AboutOverlay.Visibility = Visibility.Collapsed;
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

    private void SettingsToc_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name && FindName(name) is FrameworkElement target)
            target.BringIntoView();
    }

    /// <summary>Settings ▸ Permissions: 승인 정책 세그(ask/safe/yolo) 선택.</summary>
    private void ApprovalPolicy_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string policy)
            _vm.SettingsApprovalPolicy = policy;
    }

    /// <summary>Settings ▸ Appearance: 강조색 스와치 선택 (라이브 적용).</summary>
    private void AccentSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string accent)
            _vm.SettingsAccent = accent;
    }

    /// <summary>Settings ▸ Appearance: 밀도 세그(comfortable/compact) 선택.</summary>
    private void DensitySeg_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string density)
            _vm.SettingsDensity = density;
    }

    /// <summary>Settings ▸ Runtimes: 엔진 CLI를 새 터미널로 열어 로그인.</summary>
    private void EngineSignIn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id)
            _vm.SignIn(id);
    }

    private void HistoryRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistoryRowViewModel row)
            _vm.OpenHistoryRow(row);
    }

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

    private void Composer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
        }
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

    // ----- image attach (paste / file picker) -----
    private static readonly string AttachmentsDir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentManager", "attachments");

    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.IsComposerSuggestionOpen)
        {
            if (e.Key == Key.Up)
            {
                MoveSuggestionSelection(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down)
            {
                MoveSuggestionSelection(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                _vm.ApplySuggestion(ComposerBox);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                _vm.CloseComposerSuggestion();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && Clipboard.ContainsImage())
        {
            e.Handled = true;
            PasteClipboardImage();
        }
    }

    private void ComposerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var text = tb.Text ?? "";
        var caret = tb.CaretIndex;
        if (caret < 0 || caret > text.Length) return;

        int tokenStart = -1;
        char mode = '\0';
        for (int i = caret - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == ' ' || c == '\n' || c == '\r')
            {
                break;
            }
            if (c == '@' || c == '/')
            {
                tokenStart = i;
                mode = c;
                break;
            }
        }

        if (tokenStart != -1)
        {
            var query = text.Substring(tokenStart + 1, caret - (tokenStart + 1));
            _vm.TriggerComposerSuggestion(mode, query, tokenStart);
        }
        else
        {
            _vm.CloseComposerSuggestion();
        }
    }

    private void MoveSuggestionSelection(int direction)
    {
        if (_vm.ComposerSuggestions.Count == 0) return;
        var current = _vm.SelectedComposerSuggestion;
        int idx = current == null ? -1 : _vm.ComposerSuggestions.IndexOf(current);
        idx += direction;
        if (idx < 0) idx = _vm.ComposerSuggestions.Count - 1;
        if (idx >= _vm.ComposerSuggestions.Count) idx = 0;
        _vm.SelectedComposerSuggestion = _vm.ComposerSuggestions[idx];
    }

    private void SuggestionList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        _vm.ApplySuggestion(ComposerBox);
    }

    private void SuggestionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            _vm.ApplySuggestion(ComposerBox);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.CloseComposerSuggestion();
            e.Handled = true;
        }
    }

    private void PasteClipboardImage()
    {
        if (_vm.ActiveSession is not { } s) return;
        try
        {
            var img = Clipboard.GetImage();
            if (img is null) return;
            System.IO.Directory.CreateDirectory(AttachmentsDir);
            var file = System.IO.Path.Combine(AttachmentsDir,
                "paste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using (var fs = System.IO.File.Create(file)) enc.Save(fs);
            s.PendingImages.Add(file);
        }
        catch { }
    }

    private void AttachImage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveSession is not { } s) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = App.L("L.ImagesFilter"),
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) s.PendingImages.Add(f);
    }

    private void RemovePendingImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string path && _vm.ActiveSession is { } s)
            s.PendingImages.Remove(path);
    }

    private void ModelOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string model && _vm.ActiveSession is { } s)
            s.Model = model;
        // close the popup by unchecking its toggle (popup IsOpen is two-way bound)
        if (FindName("ModelMenuBtn") is System.Windows.Controls.Primitives.ToggleButton t)
            t.IsChecked = false;
    }

    private void EffortOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string effort && _vm.ActiveSession is { } s)
            s.ReasoningEffort = effort;
        if (FindName("EffortMenuBtn") is System.Windows.Controls.Primitives.ToggleButton t)
            t.IsChecked = false;
    }

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
            if (AboutOverlay.Visibility == Visibility.Visible) { AboutOverlay.Visibility = Visibility.Collapsed; e.Handled = true; }
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

    private void AddExtraPath_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveProject is null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = App.L("L.AddExtraFolderDialogTitle"),
            InitialDirectory = _vm.ActiveProject.Path,
        };
        if (dlg.ShowDialog(this) == true)
            _vm.AddExtraPath(dlg.FolderName);
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

    private static string BuildTranscriptMarkdown(SessionViewModel s)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# " + s.Title).AppendLine();
        foreach (var item in s.Transcript)
        {
            switch (item)
            {
                case UserBlock u:
                    sb.AppendLine("## 🧑 User").AppendLine(u.Text);
                    if (u.HasSent) sb.AppendLine().AppendLine("> sent (EN): " + u.SentText);
                    sb.AppendLine();
                    break;
                case AgentTextBlock a: sb.AppendLine("## 🤖 " + s.AgentName).AppendLine(a.Text).AppendLine(); break;
                case ToolBlock t:
                    sb.AppendLine("### 🔧 " + t.Name);
                    if (!string.IsNullOrWhiteSpace(t.Body)) sb.AppendLine("```").AppendLine(t.Body.TrimEnd()).AppendLine("```");
                    sb.AppendLine();
                    break;
                case ErrorBlock err: sb.AppendLine("### ❌ " + err.Title).AppendLine(err.Body).AppendLine(); break;
                case ApprovalBlock p: sb.AppendLine("### ⚠ Approval: " + p.ToolName + " → " + p.State).AppendLine(); break;
                case WorkingBlock w: sb.AppendLine("> " + w.Text).AppendLine(); break;
            }
        }
        return sb.ToString();
    }

    private void ExportTranscript_Click(object sender, RoutedEventArgs e)
    {
        var s = _vm.ActiveSession;
        if (s is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "session-" + s.Title + ".md",
            Filter = App.L("L.MarkdownFilter")
        };
        if (dlg.ShowDialog() != true) return;
        try { System.IO.File.WriteAllText(dlg.FileName, BuildTranscriptMarkdown(s)); } catch { }
    }

    private void CopyTranscript_Click(object sender, RoutedEventArgs e)
    {
        var s = _vm.ActiveSession;
        if (s is null) return;
        try { Clipboard.SetText(BuildTranscriptMarkdown(s)); } catch { }
    }

    private void OrchCardDiff_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionViewModel session)
        {
            _vm.ActiveSession = session;
            _vm.IsReviewOpen = true;
            _vm.CurrentView = MainViewKind.Session;
        }
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
