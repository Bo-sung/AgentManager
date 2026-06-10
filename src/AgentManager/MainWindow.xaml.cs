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
        RestoreWindowPlacement();
        Closing += (_, _) => SaveWindowPlacement();
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
        => Dispatcher.InvokeAsync(() => TranscriptScroll?.ScrollToEnd());

    private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxBtn_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void SessionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SessionViewModel s)
            _vm.ActiveSession = s;
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

    private void ModelOption_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string model && _vm.ActiveSession is { } s)
            s.Model = model;
        // close the popup by unchecking its toggle (popup IsOpen is two-way bound)
        if (FindName("ModelMenuBtn") is System.Windows.Controls.Primitives.ToggleButton t)
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
            if (_vm.ShowNewAgent) { _vm.ShowNewAgent = false; e.Handled = true; }
            else if (_vm.ShowNewProject) { _vm.ShowNewProject = false; e.Handled = true; }
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

    private void ExportTranscript_Click(object sender, RoutedEventArgs e)
    {
        var s = _vm.ActiveSession;
        if (s is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "session-" + s.Title + ".md",
            Filter = "Markdown (*.md)|*.md"
        };
        if (dlg.ShowDialog() != true) return;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# " + s.Title).AppendLine();
        foreach (var item in s.Transcript)
        {
            switch (item)
            {
                case UserBlock u: sb.AppendLine("## 🧑 User").AppendLine(u.Text).AppendLine(); break;
                case AgentTextBlock a: sb.AppendLine("## 🤖 " + s.AgentName).AppendLine(a.Text).AppendLine(); break;
                case ToolBlock t: sb.AppendLine("### 🔧 " + t.Name).AppendLine("```").AppendLine(t.Body).AppendLine("```").AppendLine(); break;
                case ErrorBlock err: sb.AppendLine("### ❌ " + err.Title).AppendLine(err.Body).AppendLine(); break;
                case ApprovalBlock p: sb.AppendLine("### ⚠ Approval: " + p.ToolName + " → " + p.State).AppendLine(); break;
                case WorkingBlock w: sb.AppendLine("> " + w.Text).AppendLine(); break;
            }
        }
        try { System.IO.File.WriteAllText(dlg.FileName, sb.ToString()); } catch { }
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

