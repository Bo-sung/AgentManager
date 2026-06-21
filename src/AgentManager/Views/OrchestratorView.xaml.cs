using System.Windows.Controls;

namespace AgentManager.Views;

/// <summary>오케스트레이터 대시보드 중앙 패널. DataContext는 부모가 AppViewModel로 주입.</summary>
public partial class OrchestratorView : UserControl
{
    public OrchestratorView() => InitializeComponent();
}
