using System.Text.RegularExpressions;

namespace DynamicDave.Umbraco.MapToRedirect.Urls;

/// <summary>Reduces old URLs to a comparable, lower-case path.</summary>
public static partial class UrlNormalizer
{
    private static readonly string[] IndexFiles = ["index.html", "index.htm", "index.php", "default.aspx"];
    private static readonly string[] Extensions = [".html", ".htm", ".php", ".aspx", ".asp"];

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();

    /// <summary>Returns the normalized path, or null when the input is empty or not a path / absolute http(s) URL.</summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = url.Trim();
        string path;

        if (SchemeRegex().IsMatch(value))
        {
            if (!TryAbsoluteHttp(value, out var uri))
            {
                return null;
            }

            path = uri.AbsolutePath;
        }
        else
        {
            var cut = value.IndexOfAny(['?', '#']);
            path = cut >= 0 ? value[..cut] : value;
        }

        path = Uri.UnescapeDataString(path).ToLowerInvariant();

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var result = segments.Length == 0 ? "/" : "/" + string.Join('/', segments);

        var last = segments.Length == 0 ? string.Empty : segments[^1];
        if (last.Length > 0)
        {
            var parent = result[..^last.Length].TrimEnd('/');
            if (IndexFiles.Contains(last))
            {
                return parent.Length == 0 ? "/" : parent;
            }

            var extension = Extensions.FirstOrDefault(e => last.Length > e.Length && last.EndsWith(e, StringComparison.Ordinal));
            if (extension is not null)
            {
                return parent + "/" + last[..^extension.Length];
            }
        }

        return result;
    }

    /// <summary>Returns the last segment of a normalized path ("/" gives an empty string).</summary>
    public static string Slug(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    /// <summary>Returns the lower-case host of an absolute http(s) URL, or null.</summary>
    public static string? HostOf(string url) =>
        TryAbsoluteHttp(url.Trim(), out var uri) ? uri.Host.ToLowerInvariant() : null;

    private static bool TryAbsoluteHttp(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.Host.Length > 0;
}
