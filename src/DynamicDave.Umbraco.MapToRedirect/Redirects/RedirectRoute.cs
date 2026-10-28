using System.Globalization;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>The route under which Umbraco stores and looks up a redirect URL.</summary>
public static class RedirectRoute
{
    /// <summary>
    /// The route of a request for a normalized path on a domain of the site, in the format Umbraco's own redirect
    /// tracking writes (verified against the TestSite on 17.7.1): the root node id followed by the path relative to the
    /// domain ("1057/over-ons"; on "klant.nl/en" the request "/en/over-ons" gives "1057/over-ons"), or the path itself
    /// for a site without domains ("/over-ons"). A path outside the domain's prefix is used unchanged.
    /// </summary>
    public static string For(SiteInfo site, CultureDomain? domain, string path)
    {
        if (site.Domains.Count == 0) return path;

        var prefix = domain?.PathPrefix ?? string.Empty;
        var relative = prefix.Length == 0 ? path : RelativeTo(prefix, path) ?? path;
        return site.RootId.ToString(CultureInfo.InvariantCulture) + relative;
    }

    /// <summary>
    /// The route of an old path on a site in a culture, without a request host (the test run). When the culture has a
    /// domain without a path prefix (or no domain of its own: Umbraco still writes the root-id form) that domain serves
    /// every path, so the route keeps the full path. When every domain of the culture has a prefix ("klant.nl/en"), a
    /// path under one of them loses the longest matching prefix ("/en/over-ons" gives "1057/over-ons"); a path outside
    /// all of them is served by no domain of the culture and has no route (null).
    /// </summary>
    public static string? For(SiteInfo site, string culture, string path)
    {
        if (site.Domains.Count == 0) return path;

        var domains = site.DomainsOf(culture);
        if (domains.Count == 0 || domains.Any(d => d.PathPrefix.Length == 0)) return For(site, (CultureDomain?)null, path);

        var domain = domains
            .Where(d => RelativeTo(d.PathPrefix, path) is not null)
            .OrderByDescending(d => d.PathPrefix.Length)
            .FirstOrDefault();
        return domain is null ? null : For(site, domain, path);
    }

    private static string? RelativeTo(string prefix, string path)
    {
        if (prefix.Length == 0) return path;
        if (string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase)) return "/";
        return path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : null;
    }
}
