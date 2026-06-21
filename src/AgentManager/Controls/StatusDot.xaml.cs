using System.Windows;
using System.Windows.Controls;

namespace AgentManager.Controls;

/// <summary>상태 닷(코어 점 + running pulse 헤일로). Status = "running"|"waiting"|"done"|"error".</summary>
public partial class StatusDot : UserControl
{
    public StatusDot() => InitializeComponent();

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(StatusDot), new PropertyMetadata(""));

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }
}
