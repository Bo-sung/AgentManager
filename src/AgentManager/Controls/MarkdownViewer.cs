using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AgentManager.Controls;

public sealed partial class MarkdownViewer : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarkdownViewer),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is MarkdownViewer viewer)
                    viewer.Render(e.NewValue as string ?? "");
            }));

    private static readonly Brush TextBrush = Brush("#DBE3EA");
    private static readonly Brush MutedBrush = Brush("#90A0AC");
    private static readonly Brush CodeBackground = Brush("#0D131A");
    private static readonly Brush CodeBorder = Brush("#22303B");
    private static readonly FontFamily Sans = new("Segoe UI, sans-serif");
    private static readonly FontFamily Mono = new("Consolas, Cascadia Mono, monospace");

    public MarkdownViewer()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        IsToolBarVisible = false;
        Focusable = false;
        Render("");
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Render(string markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextBrush,
            FontFamily = Sans,
            FontSize = 13,
            LineHeight = 21,
            ColumnWidth = 10000,
        };

        var text = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                document.Blocks.Add(CodeBlock(string.Join(Environment.NewLine, codeLines)));
                continue;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                document.Blocks.Add(HeadingBlock(heading.Groups[1].Value.Length, heading.Groups[2].Value.Trim()));
                i++;
                continue;
            }

            if (BulletRegex().IsMatch(line))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(18, 0, 0, 8), Padding = new Thickness(0) };
                while (i < lines.Length && BulletRegex().Match(lines[i]) is { Success: true } bullet)
                {
                    var paragraph = Paragraph(0, 2);
                    AddInlineRuns(paragraph, bullet.Groups[1].Value.Trim());
                    list.ListItems.Add(new ListItem(paragraph));
                    i++;
                }
                document.Blocks.Add(list);
                continue;
            }

            if (NumberRegex().IsMatch(line))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Decimal, Margin = new Thickness(18, 0, 0, 8), Padding = new Thickness(0) };
                while (i < lines.Length && NumberRegex().Match(lines[i]) is { Success: true } numbered)
                {
                    var paragraph = Paragraph(0, 2);
                    AddInlineRuns(paragraph, numbered.Groups[1].Value.Trim());
                    list.ListItems.Add(new ListItem(paragraph));
                    i++;
                }
                document.Blocks.Add(list);
                continue;
            }

            var parts = new List<string>();
            while (i < lines.Length
                   && !string.IsNullOrWhiteSpace(lines[i])
                   && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)
                   && !HeadingRegex().IsMatch(lines[i])
                   && !BulletRegex().IsMatch(lines[i])
                   && !NumberRegex().IsMatch(lines[i]))
            {
                parts.Add(lines[i].Trim());
                i++;
            }

            var p = Paragraph(0, 8);
            AddInlineRuns(p, string.Join(" ", parts));
            document.Blocks.Add(p);
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(Paragraph(0, 0));
        Document = document;
    }

    private static Paragraph HeadingBlock(int level, string text)
    {
        var p = Paragraph(0, 9);
        p.FontWeight = FontWeights.SemiBold;
        p.FontSize = level switch { 1 => 18, 2 => 16, 3 => 14.5, _ => 13.5 };
        AddInlineRuns(p, text);
        return p;
    }

    private static BlockUIContainer CodeBlock(string code)
    {
        var box = new Border
        {
            Background = CodeBackground,
            BorderBrush = CodeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 3, 0, 10),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = Mono,
                FontSize = 12,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
            }
        };
        return new BlockUIContainer(box) { Margin = new Thickness(0) };
    }

    private static Paragraph Paragraph(double top, double bottom) =>
        new() { Margin = new Thickness(0, top, 0, bottom), Foreground = TextBrush };

    private static void AddInlineRuns(Paragraph paragraph, string text)
    {
        var index = 0;
        foreach (Match match in InlineRegex().Matches(text))
        {
            if (match.Index > index)
                paragraph.Inlines.Add(new Run(text[index..match.Index]));

            var token = match.Value;
            if (token.StartsWith('`') && token.EndsWith('`') && token.Length >= 2)
            {
                paragraph.Inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = Mono,
                    FontSize = 12,
                    Background = CodeBackground,
                    Foreground = MutedBrush,
                });
            }
            else if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal) && token.Length >= 4)
            {
                paragraph.Inlines.Add(new Run(token[2..^2]) { FontWeight = FontWeights.SemiBold });
            }
            else
            {
                paragraph.Inlines.Add(new Run(token));
            }
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            paragraph.Inlines.Add(new Run(text[index..]));
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-*]\s+(.+)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*\d+\.\s+(.+)$")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"`[^`\n]+`|\*\*[^*\n]+\*\*")]
    private static partial Regex InlineRegex();
}
