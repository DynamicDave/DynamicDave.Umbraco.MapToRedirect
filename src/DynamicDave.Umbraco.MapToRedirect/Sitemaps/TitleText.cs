using System.Net;
using System.Text;

namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

/// <summary>Extracts a page's human title from its HTML. Works with linear scans only; the input is untrusted.</summary>
public static class TitleText
{
    private const int MaxScanLength = 512 * 1024;
    private static readonly string[] Separators = [" | ", " - ", " – "];
    private static readonly string[] SkippedElements = ["script", "style", "svg"];

    /// <summary>
    /// The <c>&lt;title&gt;</c> with the site suffix (after the last " | ", " - " or " – ") removed, else the text of the
    /// first <c>&lt;h1&gt;</c>; HTML-decoded with whitespace collapsed. Comments, script, style and svg content is ignored.
    /// Null when neither has text.
    /// </summary>
    public static string? Extract(string html)
    {
        var visible = StripInvisible(html.Length > MaxScanLength ? html[..MaxScanLength] : html);

        var title = Clean(ElementText(visible, "title"));
        if (title is not null)
        {
            var cut = Separators.Max(s => title.LastIndexOf(s, StringComparison.Ordinal));
            return cut > 0 ? title[..cut].TrimEnd() : title;
        }

        return Clean(ElementText(visible, "h1"));
    }

    /// <summary>Removes comments and the content of script, style and svg elements.</summary>
    private static string StripInvisible(string html)
    {
        var result = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                result.Append(html, i, html.Length - i);
                break;
            }

            result.Append(html, i, lt - i);
            if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }

            var skipped = SkippedElements.FirstOrDefault(name => IsTagStart(html, lt, name));
            if (skipped is not null)
            {
                var close = html.IndexOf("</" + skipped, lt + 1 + skipped.Length, StringComparison.OrdinalIgnoreCase);
                var closeEnd = close < 0 ? -1 : html.IndexOf('>', close);
                i = closeEnd < 0 ? html.Length : closeEnd + 1;
                continue;
            }

            result.Append('<');
            i = lt + 1;
        }

        return result.ToString();
    }

    /// <summary>True when <paramref name="html"/> has "&lt;name" at <paramref name="index"/> and the tag name ends right there.</summary>
    private static bool IsTagStart(string html, int index, string name)
    {
        var end = index + 1 + name.Length;
        return end <= html.Length
               && string.Compare(html, index + 1, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0
               && (end == html.Length || char.IsWhiteSpace(html[end]) || html[end] is '>' or '/');
    }

    private static string? ElementText(string html, string name)
    {
        var from = 0;
        while (from < html.Length)
        {
            var lt = html.IndexOf('<', from);
            if (lt < 0)
            {
                return null;
            }

            from = lt + 1;
            if (!IsTagStart(html, lt, name))
            {
                continue;
            }

            var open = html.IndexOf('>', lt);
            if (open < 0)
            {
                return null;
            }

            var close = html.IndexOf("</" + name, open + 1, StringComparison.OrdinalIgnoreCase);
            return close < 0 ? null : StripTags(html.AsSpan(open + 1, close - open - 1));
        }

        return null;
    }

    private static string StripTags(ReadOnlySpan<char> value)
    {
        var text = new StringBuilder(value.Length);
        var inTag = false;
        foreach (var c in value)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>' && inTag)
            {
                inTag = false;
            }
            else if (!inTag)
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static string? Clean(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(raw);
        var text = new StringBuilder(decoded.Length);
        var space = false;
        foreach (var c in decoded)
        {
            if (char.IsWhiteSpace(c))
            {
                space = text.Length > 0;
                continue;
            }

            if (space)
            {
                text.Append(' ');
                space = false;
            }

            text.Append(c);
        }

        return text.Length == 0 ? null : text.ToString();
    }
}
