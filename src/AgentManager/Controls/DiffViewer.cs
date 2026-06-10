using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AgentManager.Controls;

public sealed class DiffViewer : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(DiffViewer),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is DiffViewer viewer)
                    viewer.Render(e.NewValue as string ?? "");
            }));

    public DiffViewer()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        IsToolBarVisible = false;
        Focusable = false;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Render("");
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Render(string diffText)
    {
        var monoFont = new FontFamily("Consolas");
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = monoFont,
            FontSize = 11.5,
            LineHeight = 11.5 * 1.6,
            ColumnWidth = 100000,
            PageWidth = 100000,
        };

        var txt1Brush = GetThemeBrush("Txt1", "#FF90A0AC");
        var txt2Brush = GetThemeBrush("Txt2", "#FF5E6E7A");
        var infoBrush = GetThemeBrush("Info", "#FF5B9BFF");
        var bg3Brush = GetThemeBrush("Bg3", "#FF161F29");

        var addFg = Brush("#FF7FE0B6");
        var addBg = Brush("#1A2FAE7A");
        var delFg = Brush("#FFEA97A1");
        var delBg = Brush("#17E05566");

        var text = (diffText ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');

        foreach (var line in lines)
        {
            var p = new Paragraph
            {
                Margin = new Thickness(0),
                Padding = new Thickness(12, 1, 12, 1),
            };

            Brush fg;
            Brush? bg = null;

            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
            {
                fg = txt2Brush;
            }
            else if (line.StartsWith("+", StringComparison.Ordinal))
            {
                fg = addFg;
                bg = addBg;
            }
            else if (line.StartsWith("-", StringComparison.Ordinal))
            {
                fg = delFg;
                bg = delBg;
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                fg = infoBrush;
                bg = bg3Brush;
            }
            else
            {
                fg = txt1Brush;
            }

            p.Foreground = fg;
            if (bg != null)
            {
                p.Background = bg;
            }

            p.Inlines.Add(new Run(line));
            document.Blocks.Add(p);
        }

        if (document.Blocks.Count == 0)
        {
            var p = new Paragraph(new Run(""))
            {
                Margin = new Thickness(0),
                Padding = new Thickness(12, 1, 12, 1),
                Foreground = txt1Brush
            };
            document.Blocks.Add(p);
        }

        Document = document;
    }

    private static Brush GetThemeBrush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return Brush(fallbackHex);
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
