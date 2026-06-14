using System.Windows;
using System.Windows.Controls;

namespace AgentManager.Controls;

/// <summary>PasswordBox.Password는 의존 속성이 아니라 직접 바인딩이 불가능하다. 이 첨부 속성으로
/// VM(SettingsApiKeyCc 등)과 양방향 동기화해, API 키를 마스킹(•)하면서도 바인딩으로 다룬다.
/// 기본값을 null로 둬서 VM이 ""(빈 키)를 밀어도 변경 콜백이 한 번은 발생 → PasswordChanged 후킹이 보장된다.</summary>
public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword", typeof(string), typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(null, OnBoundPasswordChanged));

    private static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached(
            "Updating", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;
        // 피드백 루프 방지: 변경이 사용자 입력(PasswordChanged)에서 온 경우는 되돌려 쓰지 않는다.
        if ((bool)pb.GetValue(UpdatingProperty)) return;

        pb.PasswordChanged -= HandlePasswordChanged;
        var next = (string)(e.NewValue ?? "");
        if (pb.Password != next) pb.Password = next;
        pb.PasswordChanged += HandlePasswordChanged;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        var pb = (PasswordBox)sender;
        pb.SetValue(UpdatingProperty, true);
        SetBoundPassword(pb, pb.Password);
        pb.SetValue(UpdatingProperty, false);
    }
}
