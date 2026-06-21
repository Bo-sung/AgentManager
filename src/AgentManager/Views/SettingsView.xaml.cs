using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentManager.ViewModels;

namespace AgentManager.Views;

/// <summary>Settings 중앙 패널. DataContext는 부모(MainWindow)가 AppViewModel로 주입한다.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private AppViewModel? Vm => DataContext as AppViewModel;

    /// <summary>좌측 TOC: Tag(섹션 x:Name)로 해당 StackPanel을 스크롤 영역에 가져온다.</summary>
    private void SettingsToc_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name && FindName(name) is FrameworkElement target)
            target.BringIntoView();
    }

    /// <summary>Project: 추가 폴더(Extra path) 선택.</summary>
    private void AddExtraPath_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || vm.ActiveProject is null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = AgentManager.App.L("L.AddExtraFolderDialogTitle"),
            InitialDirectory = vm.ActiveProject.Path,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            vm.AddExtraPath(dlg.FolderName);
    }
}
