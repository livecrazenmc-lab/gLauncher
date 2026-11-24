using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GLauncher.Controls;

/// <summary>
/// Показывает простой HTML (заголовки, абзацы, списки, жирный/курсив) как документ в стиле лаунчера.
/// Используется для списков изменений Mojang. В XAML: &lt;controls:HtmlText Html="{Binding Body}" /&gt;
/// </summary>
public sealed class HtmlText : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(
        nameof(Html), typeof(string), typeof(HtmlText), new PropertyMetadata(null, (d, e) => ((HtmlText)d).Render((string?)e.NewValue)));

    public string? Html { get => (string?)GetValue(HtmlProperty); set => SetValue(HtmlProperty, value); }

    private static readonly Regex TagRegex = new(@"<(/?)([a-zA-Z0-9]+)[^>]*>|([^<]+)", RegexOptions.Compiled);

    public HtmlText()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsToolBarVisible = false;
        Focusable = false;
    }

    private void Render(string? html)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            PagePadding = new Thickness(14, 10, 14, 10),
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Brush.Text");
        Document = doc;
        if (string.IsNullOrWhiteSpace(html)) return;

        Paragraph? paragraph = null;
        List? list = null;
        var bold = 0; var italic = 0;
        double? headingSize = null;

        Paragraph NewParagraph(double? size = null)
        {
            var p = new Paragraph { Margin = new Thickness(0, size is null ? 0 : 10, 0, 6) };
            if (size is { } s)
            {
                p.FontSize = s;
                p.FontWeight = FontWeights.Bold;
                p.SetResourceReference(TextElement.ForegroundProperty, "Brush.TextGold");
            }
            if (list is not null)
            {
                var item = new ListItem(p);
                list.ListItems.Add(item);
            }
            else
            {
                doc.Blocks.Add(p);
            }
            return p;
        }

        foreach (Match m in TagRegex.Matches(html))
        {
            if (m.Groups[3].Success)
            {
                var text = WebUtility.HtmlDecode(m.Groups[3].Value);
                if (string.IsNullOrWhiteSpace(text) && paragraph is null) continue;
                paragraph ??= NewParagraph(headingSize);
                var run = new Run(text);
                if (bold > 0) run.FontWeight = FontWeights.Bold;
                if (italic > 0) run.FontStyle = FontStyles.Italic;
                paragraph.Inlines.Add(run);
                continue;
            }

            var closing = m.Groups[1].Value == "/";
            switch (m.Groups[2].Value.ToLowerInvariant())
            {
                case "h1" or "h2" or "h3" or "h4":
                    headingSize = closing ? null : m.Groups[2].Value.ToLowerInvariant() switch { "h1" => 20, "h2" => 17, _ => 15 };
                    paragraph = null;
                    break;
                case "p" or "div":
                    paragraph = null;
                    break;
                case "br":
                    paragraph?.Inlines.Add(new LineBreak());
                    break;
                case "ul" or "ol":
                    if (closing) { list = null; }
                    else
                    {
                        list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(18, 0, 0, 0) };
                        doc.Blocks.Add(list);
                    }
                    paragraph = null;
                    break;
                case "li":
                    paragraph = closing ? null : NewParagraph();
                    if (paragraph is not null) paragraph.Margin = new Thickness(0, 1, 0, 1);
                    break;
                case "b" or "strong":
                    bold = Math.Max(0, bold + (closing ? -1 : 1));
                    break;
                case "i" or "em":
                    italic = Math.Max(0, italic + (closing ? -1 : 1));
                    break;
            }
        }
    }
}

