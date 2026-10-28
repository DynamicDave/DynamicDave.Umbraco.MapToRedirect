using System.Globalization;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>The route under which Umbraco stores and looks up a redirect URL.</summary>
public static class RedirectRoute
{
    /// <summary>
    /// The route for a normalized old path on a site in a culture, in the format Umbraco's own redirect tracking
    /// writes (verified against the TestSite on 17.7.1): the root node id followed by the path when the site has a
    /// domain ("1057/over-ons", also for a culture without a domain of its own), otherwise the path ("/over-ons").
    /// When a domain of the culture has a path prefix ("klant.nl/en") and the old path starts with it, the prefix is
    /// removed ("/en/over-ons" gives "1057/over-ons"): Umbraco looks redirects up by the path relative to the domain.
    /// </summary>
    public static string For(SiteInfo site, string culture, string path)
    {
        if (site.Domains.Count == 0) return path;

        var relative = PrefixesOf(site, culture)
            .OrderByDescending(prefix => prefix.Length)
            .Select(prefix => RelativeTo(prefix, path))
            .FirstOrDefault(rest => rest is not null) ?? path;

        return site.RootId.ToString(CultureInfo.InvariantCulture) + relative;
    }

    private static IEnumerable<string> PrefixesOf(SiteInfo site, string culture)
    {
        if (site.PathPrefixesByCulture is not { } byCulture) return [];
        return byCulture.TryGetValue(culture, out var prefixes)
            ? prefixes
            : byCulture.FirstOrDefault(p => string.Equals(p.Key, culture, StringComparison.OrdinalIgnoreCase)).Value ?? [];
    }

    private static string? RelativeTo(string prefix, string path)
    {
        if (string.Equals(path, prefix, StringComparison.Ordinal)) return "/";
        return path.StartsWith(prefix + "/", StringComparison.Ordinal) ? path[prefix.Length..] : null;
    }
}
