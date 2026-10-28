namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>Sites, their cultures and their published pages, read from the published content cache.</summary>
public interface ISiteCatalog
{
    IReadOnlyList<SiteInfo> GetSites();

    SiteInfo? GetSite(Guid rootKey);

    /// <summary>
    /// All pages of the site published and reachable (with a URL) in the culture. Build once per job and pass the
    /// returned list unchanged to <see cref="Resolve"/> and <see cref="IsPublishedPath"/>: their lookup index is keyed
    /// on that list instance.
    /// </summary>
    IReadOnlyList<SitePage> GetPages(SiteInfo site, string culture);

    /// <summary>
    /// Resolves a CSV "New URL" (document key, full URL on one of the site's domains for <paramref name="culture"/>,
    /// or path) to one of <paramref name="pages"/> (the list from <see cref="GetPages"/> for that culture); null when
    /// it does not resolve. A full URL on a domain of another culture of the same site does not resolve.
    /// </summary>
    SitePage? Resolve(SiteInfo site, string culture, IReadOnlyList<SitePage> pages, string newUrl);

    /// <summary>
    /// True when a page in <paramref name="pages"/> (the list from <see cref="GetPages"/>) is published at the
    /// (normalized) path.
    /// </summary>
    bool IsPublishedPath(IReadOnlyList<SitePage> pages, string path);
}
