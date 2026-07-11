using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AgentManager.Persistence;
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

    private void ExportTranscript_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ActiveSession is not { } s) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "session-" + s.Title + ".md",
            Filter = AgentManager.App.L("L.MarkdownFilter")
        };
        if (dlg.ShowDialog() != true) return;
        try { System.IO.File.WriteAllText(dlg.FileName, TranscriptExporter.ToMarkdown(s)); } catch { }
    }

    private void CopyTranscript_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ActiveSession is not { } s) return;
        try { Clipboard.SetText(TranscriptExporter.ToMarkdown(s)); } catch { }
    }

    // ----- 컴포저: 전송 / 받아쓰기 / @·/ 서제스천 / 이미지 -----
    // (엔터 전송은 Composer_PreviewKeyDown에서 처리 — AcceptsReturn TextBox가 KeyDown을 먼저 가로채기 때문)

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

    private void Composer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is not null && vm.IsComposerSuggestionOpen)
        {
            if (e.Key == Key.Up) { MoveSuggestionSelection(-1); e.Handled = true; return; }
            if (e.Key == Key.Down) { MoveSuggestionSelection(1); e.Handled = true; return; }
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                // Popup open but nothing applicable (empty/no-match list, no selection):
                // don't steal the key — close it and let Enter fall through to send.
                // (Tab just closes; it never sends.)
                if (vm.SelectedComposerSuggestion is null || vm.ComposerSuggestions.Count == 0)
                {
                    vm.CloseComposerSuggestion();
                    if (e.Key == Key.Tab) { e.Handled = true; return; }
                }
                else
                {
                    ApplySuggestionToComposer();
                    e.Handled = true;
                    return;
                }
            }
            if (e.Key == Key.Escape) { vm.CloseComposerSuggestion(); e.Handled = true; return; }
        }

        // Enter sends; Shift+Enter inserts a newline. Must be in PreviewKeyDown: the
        // AcceptsReturn TextBox marks Enter handled in its own KeyDown, so a bubbling
        // handler would never see it (and a newline would be inserted instead of sending).
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (Vm is { } v && v.SendCommand.CanExecute(null)) v.SendCommand.Execute(null);
            return;
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

        // Only a freshly TYPED single character may open the popup. Paste, multi-char
        // inserts and deletions must not — otherwise a trigger char (@ / >) that merely
        // sits before the caret (e.g. a pasted "PS ...>" log) opens the popup and steals
        // Enter. Filtering still works because once open, every edit keeps updating it.
        var changes = e.Changes;
        bool isSingleTypedInsert = changes.Count == 1
            && changes.First().AddedLength == 1
            && changes.First().RemovedLength == 0;

        if (vm.IsComposerSuggestionOpen)
        {
            // already open from active query typing — keep filtering on every edit (incl. backspace)
            vm.UpdateComposerSuggestion(tb.Text ?? "", tb.CaretIndex);
        }
        else if (isSingleTypedInsert)
        {
            vm.UpdateComposerSuggestion(tb.Text ?? "", tb.CaretIndex);
        }
        else
        {
            vm.CloseComposerSuggestion();
        }
    }

    /// <summary>VM에서 서제스천을 Draft에 적용한 뒤, 반환된 캐럿 위치로 TextBox 포커스/캐럿을 맞춘다.</summary>
    private void ApplySuggestionToComposer()
    {
        if (Vm is not { } vm) return;
        var caret = vm.ApplySelectedSuggestion();
        if (caret >= 0)
        {
            ComposerBox.CaretIndex = Math.Min(caret, ComposerBox.Text?.Length ?? 0);
            ComposerBox.Focus();
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
        => ApplySuggestionToComposer();

    private void SuggestionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Enter || e.Key == Key.Tab) { ApplySuggestionToComposer(); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CloseComposerSuggestion(); e.Handled = true; }
    }

    private void PasteClipboardImage()
    {
        if (Vm?.ActiveSession is not { } s) return;
        var img = Clipboard.GetImage();
        if (img is not null && ImageAttachmentStore.SavePng(img) is { } path)
            s.PendingAttachments.Add(new PendingAttachment(path, IsImage: true));
    }

    private void AttachImage_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.ActiveSession is not { } s) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = AgentManager.App.L("L.AttachFilter"),
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames)
            s.PendingAttachments.Add(new PendingAttachment(f, AgentManager.Attachments.IsImage(f)));
    }

    private void RemovePendingImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PendingAttachment a && Vm?.ActiveSession is { } s)
            s.PendingAttachments.Remove(a);
    }

    // 파일 드래그앤드롭 → 첨부(파일 픽커와 동일 처리). Preview(터널)로 자식 TextBox보다 먼저 가로챈다.
    private void Session_DragOver(object sender, DragEventArgs e)
    {
        if (Vm?.ActiveSession is not null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true; // 파일 드롭만 가로채고, 텍스트 드래그는 TextBox에 맡긴다
        }
    }

    private void Session_Drop(object sender, DragEventArgs e)
    {
        if (Vm?.ActiveSession is not { } s) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files)
            if (System.IO.File.Exists(f)) // 폴더는 건너뜀
                s.PendingAttachments.Add(new PendingAttachment(f, AgentManager.Attachments.IsImage(f)));
        e.Handled = true;
    }

    private void ModelOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string model && Vm?.ActiveSession is { } s)
            s.Model = model;
        if (ModelMenuBtn is { } t) t.IsChecked = false; // close the popup (IsOpen is two-way bound)
    }

    /// <summary>컴포저 모델 메뉴의 "직접 입력" — 목록에 없는 모델명(예: gpt-5.7)을 Enter로 적용. 엔진 모델 필드는
    /// 자유 입력이라(정적 목록에 가두지 않음) 새 모델이 나와도 코드 수정 없이 바로 쓸 수 있게 한다.</summary>
    private void CustomModel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (sender is TextBox box && box.Text.Trim() is { Length: > 0 } model && Vm?.ActiveSession is { } s)
        {
            s.Model = model;
            box.Text = "";
        }
        if (ModelMenuBtn is { } t) t.IsChecked = false;
    }

    private void EffortOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string effort && Vm?.ActiveSession is { } s)
            s.ReasoningEffort = effort;
        if (EffortMenuBtn is { } t) t.IsChecked = false;
    }

    private void PermissionOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionViewModel.PermissionModeOption opt && Vm?.ActiveSession is { } s)
            s.PermissionMode = opt.Id;
        if (PermMenuBtn is { } t) t.IsChecked = false;
    }

    // ----- quick-reply 키보드 선택 (Claude 데스크톱식: ↑↓ 탐색 · Enter 선택 · A/B/C·숫자 · Esc) -----
    private void QuickReplyPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // 선택지가 떠 입력창이 내려가면 첫 카드에 포커스 → ↑↓ 방향키 네비게이션 시작점
        if (e.NewValue is true && sender is DependencyObject root)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FirstDescendant<Button>(root)?.Focus()));
    }

    // 페이지(질문)가 바뀌어 옵션 목록이 갱신되면 새 페이지 첫 옵션으로 포커스 이동(키보드 흐름 유지).
    private void Options_TargetUpdated(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is DependencyObject root)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FirstDescendant<Button>(root)?.Focus()));
    }

    // "기타" 자유입력 박스: Enter는 위저드-인지 전송(현재 질문 답으로 기록 후 진행), Esc는 직접입력 취소.
    private void ChoiceOther_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Vm?.DismissChoiceCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (Vm is { } v && v.ChoiceFreeInputCommand.CanExecute(null)) v.ChoiceFreeInputCommand.Execute(null);
        }
    }

    private void QuickReply_KeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || vm.ActiveSession?.ActiveChoice is not { } flow) return;
        if (Keyboard.FocusedElement is TextBox) return; // "기타" 입력 중엔 타이핑을 마커키로 가로채지 않음

        if (e.Key == Key.Escape) { vm.DismissChoiceCommand.Execute(null); e.Handled = true; return; } // → 직접 입력 복귀

        // Ctrl+Enter → 멀티 제출 (어디서든)
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        { if (flow.Multi) { vm.ChoiceSubmitCommand.Execute(null); e.Handled = true; } return; }

        // Space는 포커스된 옵션 Button이 자체 Click으로 활성화 → 중복 방지 위해 통과시킨다.
        if (e.Key == Key.Space) return;

        // Enter: 포커스된 옵션이면 활성화(Button은 Enter로 클릭하지 않음), 아니면 멀티 제출.
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.FocusedElement as FrameworkElement)?.DataContext is AgentManager.ViewModels.ChoiceOption fo)
            { vm.ActivateChoice(fo); e.Handled = true; }
            else if (flow.Multi) { vm.ChoiceSubmitCommand.Execute(null); e.Handled = true; }
            return;
        }

        if (MarkerForKey(e.Key) is not { } marker) return; // 1–9 / A–Z 단축
        foreach (var o in flow.Current.Options)
            if (string.Equals(o.Marker, marker, StringComparison.OrdinalIgnoreCase))
            { vm.ActivateChoice(o); e.Handled = true; return; }
    }

    private static T? FirstDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            if (FirstDescendant<T>(c) is { } r) return r;
        }
        return null;
    }

    /// <summary>키 → 선택지 마커 문자(A–Z / 1–9). 매칭 없으면 null.</summary>
    private static string? MarkerForKey(Key k) => k switch
    {
        >= Key.A and <= Key.Z => ((char)('A' + (k - Key.A))).ToString(),
        >= Key.D1 and <= Key.D9 => ((char)('1' + (k - Key.D1))).ToString(),
        >= Key.NumPad1 and <= Key.NumPad9 => ((char)('1' + (k - Key.NumPad1))).ToString(),
        _ => null,
    };
}
