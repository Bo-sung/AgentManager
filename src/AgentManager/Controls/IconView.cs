using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AgentManager.Controls;

/// <summary>원본 디자인(am-data.jsx)의 CIcon 대응. Theme/Icons.xaml의 24x24 Geometry를
/// Foreground 색 선(stroke 1.6, round cap/join)으로 그린다. Foreground는 부모에서 상속되므로
/// 버튼 hover/on 트리거 색이 그대로 아이콘에 반영된다(currentColor 동작).</summary>
public sealed class IconView : Control
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(IconView));

    /// <summary>stop처럼 면으로 채우는 아이콘은 true (stroke 대신 fill).</summary>
    public static readonly DependencyProperty FilledProperty =
        DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(IconView), new PropertyMetadata(false));

    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    static IconView()
    {
        WidthProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(16.0));
        IsTabStopProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
        FocusableProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
    }
}
