using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>The route under which Umbraco stores and looks up a redirect URL.</summary>
public static class RedirectRoute
{
    /// <summary>
    /// The route for a normalized old path on a site: the root node id followed by the path when the site has a
    /// domain ("1057/over-ons"), otherwise the path ("/over-ons"). This is the format Umbraco's own redirect tracking
    /// writes (verified against the TestSite on 17.7.1, also for a culture without a domain on a root that has one).
    /// The path is used as given; nothing is stripped from it.
    /// </summary>
    public static string For(SiteInfo site, string path) =>
        site.Domains.Count > 0 ? site.RootId.ToString(System.Globalization.CultureInfo.InvariantCulture) + path : path;
}
