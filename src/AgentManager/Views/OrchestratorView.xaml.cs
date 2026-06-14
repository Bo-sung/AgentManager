using System.Windows;
using System.Windows.Controls;
using AgentManager.ViewModels;

namespace AgentManager.Views;

/// <summary>오케스트레이터 대시보드 중앙 패널. DataContext는 부모가 AppViewModel로 주입.</summary>
public partial class OrchestratorView : UserControl
{
    public OrchestratorView() => InitializeComponent();

    /// <summary>카드의 diff 버튼: 해당 세션을 활성화하고 리뷰 패널을 열어 세션 뷰로 전환.</summary>
    private void OrchCardDiff_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppViewModel vm && (sender as FrameworkElement)?.DataContext is SessionViewModel session)
        {
            vm.ActiveSession = session;
            vm.IsReviewOpen = true;
            vm.CurrentView = MainViewKind.Session;
        }
    }
}
