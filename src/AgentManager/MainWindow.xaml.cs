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
}
