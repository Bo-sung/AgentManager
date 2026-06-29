using System;
using System.Collections.Generic;
using System.Linq;
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

    // 테마(다크/라이트) 리소스에서 조회 — 시작 시 팔레트가 고정되므로 매 호출 조회 비용은 무시 가능
    private static Brush R(string key, string fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? Brush(fallback);
    private static Brush TextBrush => R("Txt0", "#DBE3EA");
    private static Brush MutedBrush => R("Txt1", "#90A0AC");
    private static Brush CodeBackground => R("Bg1", "#0D131A");
    private static Brush CodeBorder => R("Line", "#22303B");
    private static readonly FontFamily Sans = new("Segoe UI, sans-serif");
    private static readonly FontFamily Mono = new("Consolas, Cascadia Mono, monospace");

    public MarkdownViewer()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        IsToolBarVisible = false;
        // text selection needs focus (TextEditor focuses on mouse-down); keep selectable but out of tab order
        Focusable = true;
        IsTabStop = false;
        IsSelectionEnabled = true;
        SelectionBrush = Brush("#3DFF5A2C");
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

            var fence = LeadingBacktickRun(line);
            if (fence >= 3)
            {
                // A bare fence (just backticks, no language tag) with no matching close ahead is almost
                // always a STRAY close left by a mid-line open — e.g. a translation glued "text: ```lang"
                // onto one line, so the real open was never recognized. Treating it as an open would
                // swallow the rest of the message as one code block. Open a block only when there is a
                // language/info tag (covers a still-streaming block) or a real closing fence ahead;
                // otherwise skip the stray fence and render the rest as normal markdown.
                var info = line.TrimStart()[fence..].Trim();
                if (info.Length == 0 && !HasClosingFence(lines, i + 1, fence)) { i++; continue; }

                // Variable-length fence: close only on a fence of equal-or-greater length, so a
                // prompt that itself contains ``` code blocks stays one (copyable) block.
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && LeadingBacktickRun(lines[i]) < fence)
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++; // skip the closing fence
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

            if (i + 1 < lines.Length &&
                lines[i].Trim().StartsWith('|') && lines[i].Trim().EndsWith('|') &&
                IsSeparatorLine(lines[i + 1]))
            {
                var headerLine = lines[i];
                var separatorLine = lines[i + 1];
                var tableLines = new List<string>();
                i += 2;
                while (i < lines.Length && lines[i].Trim().StartsWith('|') && lines[i].Trim().EndsWith('|'))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }

                document.Blocks.Add(RenderTable(headerLine, separatorLine, tableLines));
                continue;
            }

            var parts = new List<string>();
            while (i < lines.Length
                   && !string.IsNullOrWhiteSpace(lines[i])
                   && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)
                   && !HeadingRegex().IsMatch(lines[i])
                   && !BulletRegex().IsMatch(lines[i])
                   && !NumberRegex().IsMatch(lines[i])
                   && !(i + 1 < lines.Length && lines[i].Trim().StartsWith('|') && lines[i].Trim().EndsWith('|') && IsSeparatorLine(lines[i + 1])))
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
        // Read-only borderless TextBox (not a TextBlock): the FlowDocument's own selection can't reach
        // inside a BlockUIContainer, but a TextBox is independently drag-selectable, so the user can
        // copy just part of the code. The copy button still grabs the whole block in one click.
        var text = new TextBox
        {
            Text = code,
            FontFamily = Mono,
            FontSize = 12,
            Foreground = MutedBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsTabStop = false,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // A TextBox swallows the mouse wheel; re-raise it on the parent so the transcript keeps scrolling
        // when the pointer is over a code block.
        text.PreviewMouseWheel += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            if (text.Parent is UIElement parent)
                parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent });
        };

        var copyLabel = AgentManager.App.L("L.Copy");
        var copy = new Button
        {
            Content = copyLabel,
            FontFamily = Sans,
            FontSize = 10,
            Foreground = MutedBrush,
            Background = CodeBackground,
            BorderBrush = CodeBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 1, 7, 2),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 6),
            Focusable = false,
            ToolTip = copyLabel,
        };
        copy.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(code); } catch { }
            copy.Content = "✓"; // ✓
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            timer.Tick += (_, _) => { copy.Content = copyLabel; timer.Stop(); };
            timer.Start();
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(copy, 0);
        Grid.SetRow(text, 1);
        grid.Children.Add(copy);
        grid.Children.Add(text);

        var box = new Border
        {
            Background = CodeBackground,
            BorderBrush = CodeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(11, 8, 11, 9),
            Margin = new Thickness(0, 3, 0, 10),
            Child = grid,
        };
        return new BlockUIContainer(box) { Margin = new Thickness(0) };
    }

    private static Paragraph Paragraph(double top, double bottom) =>
        new() { Margin = new Thickness(0, top, 0, bottom), Foreground = TextBrush };

    private static void AddInlineRuns(InlineCollection inlines, string text)
    {
        var index = 0;
        foreach (Match match in InlineRegex().Matches(text))
        {
            if (match.Index > index)
                inlines.Add(new Run(text[index..match.Index]));

            var token = match.Value;
            if (token.StartsWith('`') && token.EndsWith('`') && token.Length >= 2)
            {
                inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = Mono,
                    FontSize = 12,
                    Background = CodeBackground,
                    Foreground = MutedBrush,
                });
            }
            else if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal) && token.Length >= 4)
            {
                inlines.Add(new Run(token[2..^2]) { FontWeight = FontWeights.SemiBold });
            }
            else if (token.StartsWith('[') && token.Contains("](http"))
            {
                var closeBracket = token.IndexOf(']');
                var linkText = token[1..closeBracket];
                var url = token[(closeBracket + 2)..^1];

                var link = new Hyperlink(new Run(linkText))
                {
                    Foreground = GetAccentBrush(),
                    TextDecorations = TextDecorations.Underline,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                try
                {
                    link.NavigateUri = new Uri(url);
                }
                catch
                {
                    // ignore invalid Uri strings
                }
                link.RequestNavigate += (s, e) =>
                {
                    Shell.Open(e.Uri.AbsoluteUri);
                    e.Handled = true;
                };
                link.Click += (s, e) =>
                {
                    Shell.Open(url);
                };
                inlines.Add(link);
            }
            else if (token.StartsWith("http://", StringComparison.Ordinal) || token.StartsWith("https://", StringComparison.Ordinal))
            {
                var url = token;
                var link = new Hyperlink(new Run(url))
                {
                    Foreground = GetAccentBrush(),
                    TextDecorations = TextDecorations.Underline,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                try
                {
                    link.NavigateUri = new Uri(url);
                }
                catch
                {
                    // ignore invalid Uri strings
                }
                link.RequestNavigate += (s, e) =>
                {
                    Shell.Open(e.Uri.AbsoluteUri);
                    e.Handled = true;
                };
                link.Click += (s, e) =>
                {
                    Shell.Open(url);
                };
                inlines.Add(link);
            }
            else
            {
                inlines.Add(new Run(token));
            }
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            inlines.Add(new Run(text[index..]));
    }

    private static void AddInlineRuns(Paragraph paragraph, string text)
    {
        AddInlineRuns(paragraph.Inlines, text);
    }

    private static Brush GetAccentBrush()
    {
        if (Application.Current?.TryFindResource("Accent") is Brush brush)
            return brush;
        return Brush("#FFFF5A2C");
    }

    private static bool IsSeparatorLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|')) return false;
        var cleaned = trimmed.Replace("|", "").Replace("-", "").Replace(":", "").Replace(" ", "");
        return cleaned.Length == 0 && trimmed.Contains('-');
    }

    // FlowDocument Table (not a BlockUIContainer/Grid): keeps cell text inside the document flow so it is
    // selectable and Ctrl+C-copyable (UI-island tables are skipped by the viewer's text selection).
    private static Block RenderTable(string headerLine, string separatorLine, List<string> tableLines)
    {
        var rawParts = headerLine.Split('|');
        var headers = new List<string>();
        var startIdx = headerLine.StartsWith('|') ? 1 : 0;
        var endIdx = headerLine.EndsWith('|') ? rawParts.Length - 1 : rawParts.Length;
        for (int idx = startIdx; idx < endIdx; idx++)
        {
            headers.Add(rawParts[idx].Trim());
        }

        var rows = new List<List<string>>();
        foreach (var rowLine in tableLines)
        {
            var rowParts = rowLine.Split('|');
            var cells = new List<string>();
            var rStart = rowLine.StartsWith('|') ? 1 : 0;
            var rEnd = rowLine.EndsWith('|') ? rowParts.Length - 1 : rowParts.Length;
            for (int idx = rStart; idx < rEnd; idx++)
            {
                cells.Add(rowParts[idx].Trim());
            }
            rows.Add(cells);
        }

        var colCount = Math.Max(headers.Count, rows.Count > 0 ? rows.Max(r => r.Count) : 0);
        if (colCount == 0) colCount = 1;

        var lineSoftBrush = Application.Current?.TryFindResource("LineSoft") as Brush ?? Brush("#FF1A242D");
        var bg3Brush = Application.Current?.TryFindResource("Bg3") as Brush ?? Brush("#FF161F29");
        var txt0Brush = Application.Current?.TryFindResource("Txt0") as Brush ?? Brush("#FFDBE3EA");
        var txt1Brush = Application.Current?.TryFindResource("Txt1") as Brush ?? Brush("#FF90A0AC");

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 12) };
        for (int c = 0; c < colCount; c++)
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        // Header row — left border on the first column, top/right/bottom on all (CellSpacing=0 → no doubles)
        var headerRow = new TableRow();
        for (int c = 0; c < colCount; c++)
        {
            var p = new Paragraph { Margin = new Thickness(0), FontFamily = Sans, FontSize = 13, LineHeight = 19,
                                    Foreground = txt0Brush, FontWeight = FontWeights.SemiBold };
            AddInlineRuns(p.Inlines, c < headers.Count ? headers[c] : "");
            headerRow.Cells.Add(new TableCell(p)
            {
                Background = bg3Brush,
                BorderBrush = lineSoftBrush,
                BorderThickness = new Thickness(c == 0 ? 1 : 0, 1, 1, 1),
                Padding = new Thickness(6, 3, 6, 3),
            });
        }
        group.Rows.Add(headerRow);

        // Body rows
        foreach (var rowCells in rows)
        {
            var tr = new TableRow();
            for (int c = 0; c < colCount; c++)
            {
                var p = new Paragraph { Margin = new Thickness(0), FontFamily = Sans, FontSize = 13, LineHeight = 19, Foreground = txt1Brush };
                AddInlineRuns(p.Inlines, c < rowCells.Count ? rowCells[c] : "");
                tr.Cells.Add(new TableCell(p)
                {
                    BorderBrush = lineSoftBrush,
                    BorderThickness = new Thickness(c == 0 ? 1 : 0, 0, 1, 1),
                    Padding = new Thickness(6, 3, 6, 3),
                });
            }
            group.Rows.Add(tr);
        }

        return table;
    }

    /// <summary>Leading backtick count of a (trimmed) line — used for variable-length code fences.</summary>
    private static int LeadingBacktickRun(string line)
    {
        var s = line.TrimStart();
        int n = 0;
        while (n < s.Length && s[n] == '`') n++;
        return n;
    }

    /// <summary>Is there a fence of equal-or-greater length at/after <paramref name="from"/>? Used to
    /// tell a real (closable) code block from a stray bare ``` left by a mid-line open.</summary>
    private static bool HasClosingFence(string[] lines, int from, int fence)
    {
        for (int j = from; j < lines.Length; j++)
            if (LeadingBacktickRun(lines[j]) >= fence) return true;
        return false;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-*]\s+(.+)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*\d+\.\s+(.+)$")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"`[^`\n]+`|\*\*[^*\n]+\*\*|\[[^\]\n]+\]\(https?://[^\)\s\n]+\)|https?://[^\s\n\(\)\[\]\{\}<>]+")]
    private static partial Regex InlineRegex();
}
