using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

/// <summary>One site with a settable list of published pages; resolving uses the real SiteCatalogRules.</summary>
internal sealed class FakeSiteCatalog(SiteInfo site, IReadOnlyList<SitePage> pages) : ISiteCatalog
{
    public IReadOnlyList<SitePage> Pages { get; set; } = pages;

    public int GetPagesCalls { get; private set; }

    public IReadOnlyList<SiteInfo> GetSites() => [site];

    public SiteInfo? GetSite(Guid rootKey) => rootKey == site.RootKey ? site : null;

    public IReadOnlyList<SitePage> GetPages(SiteInfo site, string culture)
    {
        GetPagesCalls++;
        return Pages;
    }

    public SitePage? Resolve(SiteInfo site, string culture, IReadOnlyList<SitePage> pages, string newUrl) =>
        SiteCatalogRules.Resolve(site, culture, pages, newUrl);

    public bool IsPublishedPath(IReadOnlyList<SitePage> pages, string path) => SiteCatalogRules.IsPublishedPath(pages, path);
}
