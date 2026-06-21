using System.Windows.Controls;

namespace AgentManager.Views;

/// <summary>활동 내역(History) 중앙 패널. DataContext는 부모가 AppViewModel로 주입.</summary>
public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();
}
