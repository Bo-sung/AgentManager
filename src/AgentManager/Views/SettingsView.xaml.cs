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

    /// <summary>Permissions: 승인 정책 세그(ask/safe/yolo).</summary>
    private void ApprovalPolicy_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { } vm && (sender as FrameworkElement)?.Tag is string policy)
            vm.SettingsApprovalPolicy = policy;
    }

    /// <summary>Appearance: 강조색 스와치 (라이브 적용).</summary>
    private void AccentSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { } vm && (sender as FrameworkElement)?.Tag is string accent)
            vm.SettingsAccent = accent;
    }

    /// <summary>Appearance: 밀도 세그(comfortable/compact).</summary>
    private void DensitySeg_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { } vm && (sender as FrameworkElement)?.Tag is string density)
            vm.SettingsDensity = density;
    }

    /// <summary>Runtimes: 엔진 CLI를 새 터미널로 열어 로그인.</summary>
    private void EngineSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as FrameworkElement)?.Tag is string id)
            vm.SignIn(id);
    }

    /// <summary>Runtimes: 인증 모드 세그 (Tag="cc:api" → 엔진별 모드).</summary>
    private void AuthMode_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm || (sender as FrameworkElement)?.Tag is not string tag) return;
        var parts = tag.Split(':');
        if (parts.Length != 2) return;
        var (engine, mode) = (parts[0], parts[1]);
        if (engine == "cc") vm.SettingsAuthCc = mode;
        else if (engine == "gx") vm.SettingsAuthGx = mode;
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
