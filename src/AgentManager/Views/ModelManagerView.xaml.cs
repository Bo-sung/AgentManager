using System.Windows.Controls;

namespace AgentManager.Views;

/// <summary>모델 관리 하위 페이지(설정에서 진입). DataContext는 부모(MainWindow)가 AppViewModel로 주입한다.</summary>
public partial class ModelManagerView : UserControl
{
    public ModelManagerView() => InitializeComponent();
}
