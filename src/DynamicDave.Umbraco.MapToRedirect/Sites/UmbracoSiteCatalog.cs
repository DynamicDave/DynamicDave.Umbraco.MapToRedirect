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
            // Wildcard domains are included: they carry no host but set the site's culture.
            var assigned = domainService.GetAssignedDomainsAsync(rootKey, true).GetAwaiter().GetResult()
                .OrderBy(d => d.SortOrder)
                .Select(d => new SiteDomain(d.DomainName, d.IsWildcard, d.LanguageIsoCode));

            var domains = SiteCatalogRules.BuildDomains(
                assigned,
                () => defaultCulture ??= languageService.GetDefaultIsoCodeAsync().GetAwaiter().GetResult());

            sites.Add(new SiteInfo(rootKey, id.Result, NameOf(root), domains.Domains, domains.Cultures, domains.DomainsByCulture, domains.CultureDomains));
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
        SiteCatalogRules.Resolve(site, culture, pages, newUrl);

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
    /// Resolves a "New URL": a document key, a full URL whose host is a domain of the site in
    /// <paramref name="culture"/>, or a path. Returns null when the value is empty or invalid, the host is not a
    /// domain of the site in that culture, or no page in <paramref name="pages"/> (the pages of that culture) matches.
    /// </summary>
    public static SitePage? Resolve(SiteInfo site, string culture, IReadOnlyList<SitePage> pages, string newUrl)
    {
        var value = newUrl.Trim();
        if (value.Length == 0) return null;

        var index = IndexOf(pages);
        if (Guid.TryParse(value, out var key))
        {
            return index.ByKey.GetValueOrDefault(key);
        }

        if (UrlNormalizer.HostOf(value) is not null && !HostBelongsTo(site, culture, value)) return null;

        var path = UrlNormalizer.Normalize(value);
        return path is null ? null : index.ByPath.GetValueOrDefault(path);
    }

    /// <summary>
    /// True when the value is an absolute http(s) URL whose host is a domain of the site in <paramref name="culture"/>.
    /// A host of the same site in another culture does not belong: its pages are not the selected culture's pages.
    /// </summary>
    public static bool HostBelongsTo(SiteInfo site, string culture, string url) =>
        UrlNormalizer.HostOf(url) is { } host
        && HostsOf(site, culture) is { } hosts
        && hosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    // DomainsByCulture is built case-insensitive, but a hand-built SiteInfo may use any comparer.
    private static IReadOnlyList<string>? HostsOf(SiteInfo site, string culture) =>
        site.DomainsByCulture.TryGetValue(culture, out var hosts)
            ? hosts
            : site.DomainsByCulture.FirstOrDefault(p => string.Equals(p.Key, culture, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// True when a request for <paramref name="path"/> reaches a published page: compares request paths
    /// (<see cref="UrlNormalizer.RoutePath"/>), so "/about.html" is not the page at "/about".
    /// </summary>
    public static bool IsPublishedPath(IReadOnlyList<SitePage> pages, string path) =>
        UrlNormalizer.RoutePath(path) is { } routePath && IndexOf(pages).ByRoutePath.ContainsKey(routePath);

    /// <summary>The lower-case host of an Umbraco domain name ("www.klant.nl", "klant.nl/en", "https://klant.nl").</summary>
    internal static string? HostOfDomain(string domainName)
    {
        var value = domainName.Trim();
        if (value.Length == 0) return null;
        return UrlNormalizer.HostOf(value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value);
    }

    /// <summary>The normalized path of an Umbraco domain name ("klant.nl/en" gives "/en"); null when it has none.</summary>
    internal static string? PathPrefixOfDomain(string domainName)
    {
        var value = domainName.Trim();
        if (value.Length == 0) return null;
        var url = value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return UrlNormalizer.Normalize(uri.AbsolutePath) is { } path && path != "/" ? path : null;
    }

    /// <summary>
    /// Derives a site's hosts and cultures from its domains (in sort order). Wildcard domains have no host but their
    /// culture counts; a site without any domain gets <paramref name="defaultCulture"/>.
    /// </summary>
    internal static SiteDomains BuildDomains(IEnumerable<SiteDomain> domains, Func<string> defaultCulture)
    {
        var hosts = new List<string>();
        var cultures = new List<string>();
        var byCulture = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var domainsByCulture = new Dictionary<string, List<CultureDomain>>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domains)
        {
            var culture = string.IsNullOrWhiteSpace(domain.Culture) ? null : domain.Culture.Trim();
            if (culture is not null && !cultures.Contains(culture, StringComparer.OrdinalIgnoreCase)) cultures.Add(culture);

            if (domain.IsWildcard || HostOfDomain(domain.DomainName) is not { } host) continue;
            if (!hosts.Contains(host)) hosts.Add(host);
            if (culture is null) continue;

            if (!byCulture.TryGetValue(culture, out var cultureHosts)) byCulture[culture] = cultureHosts = [];
            if (!cultureHosts.Contains(host)) cultureHosts.Add(host);

            var cultureDomain = new CultureDomain(host, PathPrefixOfDomain(domain.DomainName) ?? string.Empty);
            if (!domainsByCulture.TryGetValue(culture, out var cultureDomains)) domainsByCulture[culture] = cultureDomains = [];
            if (!cultureDomains.Contains(cultureDomain)) cultureDomains.Add(cultureDomain);
        }

        if (cultures.Count == 0) cultures.Add(defaultCulture());

        return new SiteDomains(
            hosts,
            cultures,
            byCulture.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.OrdinalIgnoreCase),
            domainsByCulture.ToDictionary(p => p.Key, p => (IReadOnlyList<CultureDomain>)p.Value, StringComparer.OrdinalIgnoreCase));
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
                ByRoutePath.TryAdd(UrlNormalizer.RoutePath(page.Path) ?? page.Path, page);
            }
        }

        public Dictionary<Guid, SitePage> ByKey { get; } = new();

        public Dictionary<string, SitePage> ByPath { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SitePage> ByRoutePath { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>A domain assigned to a site root: its name, whether it is a wildcard ("*1057") and its culture.</summary>
internal record SiteDomain(string DomainName, bool IsWildcard, string? Culture);

/// <summary>The hosts, cultures and per-culture domains (host and path prefix) of a site, derived from its domains.</summary>
internal record SiteDomains(
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Cultures,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DomainsByCulture,
    IReadOnlyDictionary<string, IReadOnlyList<CultureDomain>> CultureDomains);
