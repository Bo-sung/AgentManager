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

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (_vm.ShowNewAgent) { _vm.ShowNewAgent = false; e.Handled = true; }
            else if (_vm.ShowNewProject) { _vm.ShowNewProject = false; e.Handled = true; }
        }
    }
}

