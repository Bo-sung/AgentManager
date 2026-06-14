using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentManager.ViewModels;

namespace AgentManager.Views;

/// <summary>활성 세션의 트랜스크립트 + 컴포저 패널. DataContext는 부모(Main pane)가 ActiveSession으로
/// 주입한다. AppViewModel(명령/서제스천)은 Window.DataContext로 접근한다.</summary>
public partial class SessionView : UserControl
{
    private SessionViewModel? _wiredSession;

    public SessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>AppViewModel: Window(MainWindow)의 DataContext.</summary>
    private AppViewModel? Vm => Window.GetWindow(this)?.DataContext as AppViewModel;

    // ----- 트랜스크립트 자동 스크롤 -----

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

    /// <summary>활성 세션(=DataContext)이 바뀔 때마다 새 트랜스크립트의 변경을 구독하고 끝으로 스크롤.</summary>
    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_wiredSession is not null)
            _wiredSession.Transcript.CollectionChanged -= Transcript_Changed;
        _wiredSession = DataContext as SessionViewModel;
        if (_wiredSession is not null)
        {
            _wiredSession.Transcript.CollectionChanged += Transcript_Changed;
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

    // ----- 트랜스크립트 내보내기 -----

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
        if (Vm?.ActiveSession is not { } s) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "session-" + s.Title + ".md",
            Filter = AgentManager.App.L("L.MarkdownFilter")
        };
        if (dlg.ShowDialog() != true) return;
        try { System.IO.File.WriteAllText(dlg.FileName, BuildTranscriptMarkdown(s)); } catch { }
    }

    private void CopyTranscript_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ActiveSession is not { } s) return;
        try { Clipboard.SetText(BuildTranscriptMarkdown(s)); } catch { }
    }

    // ----- 컴포저: 전송 / 받아쓰기 / @·/ 서제스천 / 이미지 -----

    private void Composer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (Vm is { } vm && vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
        }
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

    private static readonly string AttachmentsDir =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentManager", "attachments");

    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is not null && vm.IsComposerSuggestionOpen)
        {
            if (e.Key == Key.Up) { MoveSuggestionSelection(-1); e.Handled = true; return; }
            if (e.Key == Key.Down) { MoveSuggestionSelection(1); e.Handled = true; return; }
            if (e.Key == Key.Enter || e.Key == Key.Tab) { vm.ApplySuggestion(ComposerBox); e.Handled = true; return; }
            if (e.Key == Key.Escape) { vm.CloseComposerSuggestion(); e.Handled = true; return; }
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && Clipboard.ContainsImage())
        {
            e.Handled = true;
            PasteClipboardImage();
        }
    }

    private void ComposerBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (Vm is not { } vm || sender is not TextBox tb) return;
        var text = tb.Text ?? "";
        var caret = tb.CaretIndex;
        if (caret < 0 || caret > text.Length) return;

        int tokenStart = -1;
        char mode = '\0';
        for (int i = caret - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == ' ' || c == '\n' || c == '\r') break;
            if (c == '@' || c == '/') { tokenStart = i; mode = c; break; }
        }

        if (tokenStart != -1)
        {
            var query = text.Substring(tokenStart + 1, caret - (tokenStart + 1));
            vm.TriggerComposerSuggestion(mode, query, tokenStart);
        }
        else
        {
            vm.CloseComposerSuggestion();
        }
    }

    private void MoveSuggestionSelection(int direction)
    {
        if (Vm is not { } vm || vm.ComposerSuggestions.Count == 0) return;
        var current = vm.SelectedComposerSuggestion;
        int idx = current == null ? -1 : vm.ComposerSuggestions.IndexOf(current);
        idx += direction;
        if (idx < 0) idx = vm.ComposerSuggestions.Count - 1;
        if (idx >= vm.ComposerSuggestions.Count) idx = 0;
        vm.SelectedComposerSuggestion = vm.ComposerSuggestions[idx];
    }

    private void SuggestionList_DoubleClick(object sender, MouseButtonEventArgs e)
        => Vm?.ApplySuggestion(ComposerBox);

    private void SuggestionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Enter || e.Key == Key.Tab) { vm.ApplySuggestion(ComposerBox); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CloseComposerSuggestion(); e.Handled = true; }
    }

    private void PasteClipboardImage()
    {
        if (Vm?.ActiveSession is not { } s) return;
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
        if (Vm?.ActiveSession is not { } s) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = AgentManager.App.L("L.ImagesFilter"),
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) s.PendingImages.Add(f);
    }

    private void RemovePendingImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string path && Vm?.ActiveSession is { } s)
            s.PendingImages.Remove(path);
    }

    private void ModelOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string model && Vm?.ActiveSession is { } s)
            s.Model = model;
        if (ModelMenuBtn is { } t) t.IsChecked = false; // close the popup (IsOpen is two-way bound)
    }

    private void EffortOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string effort && Vm?.ActiveSession is { } s)
            s.ReasoningEffort = effort;
        if (EffortMenuBtn is { } t) t.IsChecked = false;
    }
}
