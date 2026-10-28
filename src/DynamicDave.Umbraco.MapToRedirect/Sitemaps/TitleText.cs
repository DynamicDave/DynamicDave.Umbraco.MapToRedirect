using System.Net;
using System.Text.RegularExpressions;

namespace DynamicDave.Umbraco.MapToRedirect.Sitemaps;

/// <summary>Extracts a page's human title from its HTML.</summary>
public static partial class TitleText
{
    private static readonly string[] Separators = [" | ", " - ", " – "];

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<h1\b[^>]*>(.*?)</h1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H1Regex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// The <c>&lt;title&gt;</c> with the site suffix (after the last " | ", " - " or " – ") removed, else the text of the
    /// first <c>&lt;h1&gt;</c>; HTML-decoded with whitespace collapsed. Null when neither has text.
    /// </summary>
    public static string? Extract(string html)
    {
        var title = Clean(TitleRegex().Match(html) is { Success: true } t ? t.Groups[1].Value : null);
        if (title is not null)
        {
            var cut = Separators.Max(s => title.LastIndexOf(s, StringComparison.Ordinal));
            if (cut > 0)
            {
                title = title[..cut].TrimEnd();
            }

            return title;
        }

        return Clean(H1Regex().Match(html) is { Success: true } h ? h.Groups[1].Value : null);
    }

    private static string? Clean(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var text = WhitespaceRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(raw, string.Empty)), " ").Trim();
        return text.Length == 0 ? null : text;
    }
}
