using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AgentManager.Controls;

/// <summary>
/// Attached behavior: smoothly animate a Border's BorderBrush color on hover, reproducing the
/// original CSS `transition: border-color .14s`. WPF can't animate a frozen StaticResource brush
/// in place, so the border gets a mutable clone the first time it animates.
/// Usage: controls:BorderHover.Brush="{StaticResource AccentLine}" (optional Seconds, default .14).
/// </summary>
public static class BorderHover
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush", typeof(Brush), typeof(BorderHover), new PropertyMetadata(null, OnBrushChanged));

    public static Brush? GetBrush(DependencyObject o) => (Brush?)o.GetValue(BrushProperty);
    public static void SetBrush(DependencyObject o, Brush? v) => o.SetValue(BrushProperty, v);

    public static readonly DependencyProperty SecondsProperty = DependencyProperty.RegisterAttached(
        "Seconds", typeof(double), typeof(BorderHover), new PropertyMetadata(0.14));

    public static double GetSeconds(DependencyObject o) => (double)o.GetValue(SecondsProperty);
    public static void SetSeconds(DependencyObject o, double v) => o.SetValue(SecondsProperty, v);

    private static void OnBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;
        border.MouseEnter -= OnEnter;
        border.MouseLeave -= OnLeave;
        if (e.NewValue is not null)
        {
            border.MouseEnter += OnEnter;
            border.MouseLeave += OnLeave;
        }
    }

    private static void OnEnter(object sender, RoutedEventArgs e)
    {
        if (sender is Border b && GetBrush(b) is SolidColorBrush hover)
            Animate(b, hover.Color);
    }

    private static void OnLeave(object sender, RoutedEventArgs e)
    {
        if (sender is Border b && RestColor(b) is { } rest)
            Animate(b, rest);
    }

    /// <summary>원래(쉬는) 색을 기억해 두고 반환. 첫 호출 시 현재 BorderBrush 색을 캡처.</summary>
    private static Color? RestColor(Border b)
    {
        if (b.GetValue(RestColorProperty) is Color c) return c;
        if (b.BorderBrush is SolidColorBrush scb)
        {
            b.SetValue(RestColorProperty, scb.Color);
            return scb.Color;
        }
        return null;
    }

    private static readonly DependencyProperty RestColorProperty = DependencyProperty.RegisterAttached(
        "RestColor", typeof(object), typeof(BorderHover), new PropertyMetadata(null));

    private static void Animate(Border b, Color to)
    {
        // frozen/shared 브러시는 애니메이트 불가 → 가변 클론으로 교체(한 번).
        if (b.BorderBrush is not SolidColorBrush scb || scb.IsFrozen)
        {
            _ = RestColor(b); // capture rest color before replacing
            var from = (b.BorderBrush as SolidColorBrush)?.Color ?? Colors.Transparent;
            scb = new SolidColorBrush(from);
            b.BorderBrush = scb;
        }
        scb.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, TimeSpan.FromSeconds(GetSeconds(b))) { FillBehavior = FillBehavior.HoldEnd });
    }
}
