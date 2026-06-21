using System.Windows;
using System.Windows.Media.Animation;

namespace AgentManager.Controls;

/// <summary>
/// WPF에 없는 GridLength 애니메이션. ColumnDefinition.Width 를 0↔고정폭으로 부드럽게
/// 전환(원본 .aside transition flex-basis/width .22s 재현). pixel GridLength 간 보간만 지원.
/// </summary>
public sealed class GridLengthAnimation : AnimationTimeline
{
    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));
    public GridLength From { get => (GridLength)GetValue(FromProperty); set => SetValue(FromProperty, value); }

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));
    public GridLength To { get => (GridLength)GetValue(ToProperty); set => SetValue(ToProperty, value); }

    public IEasingFunction? EasingFunction { get; set; }

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
    {
        // From/To 미지정 시 현재(기준) 값을 사용 → 중단·역방향에서도 매끄럽게 보간.
        double from = ReadLocalValue(FromProperty) != DependencyProperty.UnsetValue
            ? From.Value : ((GridLength)defaultOriginValue).Value;
        double to = ReadLocalValue(ToProperty) != DependencyProperty.UnsetValue
            ? To.Value : ((GridLength)defaultDestinationValue).Value;
        double p = clock.CurrentProgress ?? 0;
        if (EasingFunction is not null) p = EasingFunction.Ease(p);
        var v = from + (to - from) * p;
        return new GridLength(v, GridUnitType.Pixel);
    }
}
