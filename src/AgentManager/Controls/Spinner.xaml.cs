using System.Windows.Controls;

namespace AgentManager.Controls;

/// <summary>회전 로딩 스피너 (원본 .spin 재현). 보이는 동안 0.7s로 무한 회전.</summary>
public partial class Spinner : UserControl
{
    public Spinner() => InitializeComponent();
}
