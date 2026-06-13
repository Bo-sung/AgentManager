using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AgentManager.Controls;

/// <summary>두 값이 같으면 AccentLine 브러시, 아니면 Line 브러시를 반환.
/// New Agent 런타임 카드의 선택 하이라이트에 사용 (item.Id vs 선택 엔진 Id).</summary>
public sealed class SelectedBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var match = values.Length == 2
            && values[0] is string a && values[1] is string b
            && string.Equals(a, b, StringComparison.Ordinal);
        return Application.Current.Resources[match ? "AccentLine" : "Line"];
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
