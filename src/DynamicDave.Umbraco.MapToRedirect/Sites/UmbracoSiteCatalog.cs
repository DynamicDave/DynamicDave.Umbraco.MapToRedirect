using System.Runtime.CompilerServices;
using DynamicDave.Umbraco.MapToRedirect.Urls;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;

namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>
/// Reads sites and their published pages from Umbraco's published content cache and navigation structure.
/// Only members present (with the same declaring type) in Umbraco 17.0.0 and 18 are compiled against; the
/// IPublishedContent properties that moved to IPublishedElement in 18 are read through <see cref="InterfaceProperty"/>.
/// </summary>
public sealed class UmbracoSiteCatalog(
    IUmbracoContextFactory umbracoContextFactory,
    IDocumentNavigationQueryService navigation,
    IIdKeyMap idKeyMap,
    IDomainService domainService,
    ILanguageService languageService,
    IPublishedUrlProvider urlProvider) : ISiteCatalog
{
    private static readonly string[] TitleAliases = ["title", "pageTitle", "metaTitle", "seoTitle"];

    private static readonly Func<IPublishedContent, string> NameOf = InterfaceProperty.Getter<IPublishedContent, string>("Name");

    private static readonly Func<IPublishedContent, IReadOnlyDictionary<string, PublishedCultureInfo>> CulturesOf =
        InterfaceProperty.Getter<IPublishedContent, IReadOnlyDictionary<string, PublishedCultureInfo>>("Cultures");

    public IReadOnlyList<SiteInfo> GetSites()
    {
        if (!navigation.TryGetRootKeys(out var rootKeys)) return [];

        using var reference = umbracoContextFactory.EnsureUmbracoContext();
        var cache = reference.UmbracoContext.Content;
        if (cache is null) return [];

        var sites = new List<SiteInfo>();
        string? defaultCulture = null;
        foreach (var rootKey in rootKeys)
        {
            var root = cache.GetById(false, rootKey);
            if (root is null) continue;

            var id = idKeyMap.GetIdForKey(rootKey, UmbracoObjectTypes.Document);
            if (!id.Success) continue;

            // Sync over async: the catalog is used from background jobs and request handlers without a
            // synchronization context.
            var domains = domainService.GetAssignedDomainsAsync(rootKey, false).GetAwaiter().GetResult()
                .Where(d => !d.IsWildcard)
                .OrderBy(d => d.SortOrder)
                .ToList();

            var hosts = domains.Select(d => SiteCatalogRules.HostOfDomain(d.DomainName)).OfType<string>()
                .Distinct(StringComparer.Ordinal).ToList();
            var cultures = domains.Select(d => d.LanguageIsoCode).OfType<string>().Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (cultures.Count == 0)
            {
                defaultCulture ??= languageService.GetDefaultIsoCodeAsync().GetAwaiter().GetResult();
                cultures.Add(defaultCulture);
            }

            sites.Add(new SiteInfo(rootKey, id.Result, NameOf(root), hosts, cultures));
        }

        return sites;
    }

    public SiteInfo? GetSite(Guid rootKey) => GetSites().FirstOrDefault(s => s.RootKey == rootKey);

    public IReadOnlyList<SitePage> GetPages(SiteInfo site, string culture)
    {
        if (!navigation.TryGetDescendantsKeysOrSelfKeys(site.RootKey, out var keys)) return [];

        using var reference = umbracoContextFactory.EnsureUmbracoContext();
        var cache = reference.UmbracoContext.Content;
        if (cache is null) return [];

        var pages = new List<SitePage>();
        foreach (var key in keys)
        {
            if (ToPage(cache, key, culture) is { } page) pages.Add(page);
        }

        return pages.AsReadOnly();
    }

    public SitePage? Resolve(SiteInfo site, string culture, IReadOnlyList<SitePage> pages, string newUrl) =>
        SiteCatalogRules.Resolve(site, pages, newUrl);

    public bool IsPublishedPath(IReadOnlyList<SitePage> pages, string path) => SiteCatalogRules.IsPublishedPath(pages, path);

    private SitePage? ToPage(IPublishedContentCache cache, Guid key, string culture)
    {
        var content = cache.GetById(false, key);
        if (content is null) return null;

        // Variant content lists only its published cultures; invariant content has a single entry with an empty key.
        var cultures = CulturesOf(content);
        var cultureInfo = cultures.FirstOrDefault(c => string.Equals(c.Key, culture, StringComparison.OrdinalIgnoreCase)).Value;
        if (cultureInfo is null && !cultures.ContainsKey(string.Empty)) return null;

        // "#" means the page has no route in this culture (for example an unpublished ancestor).
        var url = urlProvider.GetUrl(content, UrlMode.Relative, culture, null);
        if (string.IsNullOrEmpty(url) || url == "#") return null;

        var path = UrlNormalizer.Normalize(url);
        if (path is null) return null;

        var name = cultureInfo?.Name is { Length: > 0 } cultureName ? cultureName : NameOf(content);
        return new SitePage(key, path, UrlNormalizer.Slug(path), name, TitleOf(content, culture));
    }

    private static string? TitleOf(IPublishedContent content, string culture)
    {
        foreach (var alias in TitleAliases)
        {
            if (content.GetProperty(alias)?.GetValue(culture, null) is string { } value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

/// <summary>The Umbraco-independent rules of the site catalog.</summary>
public static class SiteCatalogRules
{
    private static readonly ConditionalWeakTable<IReadOnlyList<SitePage>, PageIndex> Indexes = new();

    /// <summary>
    /// Resolves a "New URL": a document key, a full URL whose host is one of the site's domains, or a path. Returns
    /// null when the value is empty or invalid, the host belongs to another site, or no page in
    /// <paramref name="pages"/> matches.
    /// </summary>
    public static SitePage? Resolve(SiteInfo site, IReadOnlyList<SitePage> pages, string newUrl)
    {
        var value = newUrl.Trim();
        if (value.Length == 0) return null;

        var index = IndexOf(pages);
        if (Guid.TryParse(value, out var key))
        {
            return index.ByKey.GetValueOrDefault(key);
        }

        if (UrlNormalizer.HostOf(value) is not null && !HostBelongsTo(site, value)) return null;

        var path = UrlNormalizer.Normalize(value);
        return path is null ? null : index.ByPath.GetValueOrDefault(path);
    }

    /// <summary>True when the value is an absolute http(s) URL whose host is one of the site's domains.</summary>
    public static bool HostBelongsTo(SiteInfo site, string url) =>
        UrlNormalizer.HostOf(url) is { } host && site.Domains.Contains(host, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a page is published at the normalized form of <paramref name="path"/>.</summary>
    public static bool IsPublishedPath(IReadOnlyList<SitePage> pages, string path) =>
        UrlNormalizer.Normalize(path) is { } normalized && IndexOf(pages).ByPath.ContainsKey(normalized);

    /// <summary>The lower-case host of an Umbraco domain name ("www.klant.nl", "klant.nl/en", "https://klant.nl").</summary>
    internal static string? HostOfDomain(string domainName)
    {
        var value = domainName.Trim();
        if (value.Length == 0) return null;
        return UrlNormalizer.HostOf(value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value);
    }

    // Lookups run once per CSV row against the same page list, so the list is indexed once and the index lives as
    // long as the list. The first page wins when two share a path.
    private static PageIndex IndexOf(IReadOnlyList<SitePage> pages) => Indexes.GetValue(pages, static list => new PageIndex(list));

    private sealed class PageIndex
    {
        public PageIndex(IReadOnlyList<SitePage> pages)
        {
            foreach (var page in pages)
            {
                ByKey.TryAdd(page.Key, page);
                ByPath.TryAdd(UrlNormalizer.Normalize(page.Path) ?? page.Path, page);
            }
        }

        public Dictionary<Guid, SitePage> ByKey { get; } = new();

        public Dictionary<string, SitePage> ByPath { get; } = new(StringComparer.Ordinal);
    }
}
