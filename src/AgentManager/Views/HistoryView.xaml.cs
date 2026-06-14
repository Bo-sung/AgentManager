using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentManager.ViewModels;

namespace AgentManager.Views;

/// <summary>활동 내역(History) 중앙 패널. DataContext는 부모가 AppViewModel로 주입.</summary>
public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private void HistoryRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AppViewModel vm && (sender as FrameworkElement)?.DataContext is HistoryRowViewModel row)
            vm.OpenHistoryRow(row);
    }
}
