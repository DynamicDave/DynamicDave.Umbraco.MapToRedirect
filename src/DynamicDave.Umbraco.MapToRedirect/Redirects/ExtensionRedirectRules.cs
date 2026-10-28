using System.Text.RegularExpressions;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>The site and culture a request host (and path prefix) belongs to.</summary>
public sealed record SiteMatch(SiteInfo Site, string Culture);

/// <summary>The redirect lookup for a request: the route and culture to pass to <see cref="IRedirectStore.Find"/>.</summary>
public sealed record ExtensionRedirectLookup(SiteInfo Site, string Culture, string Route);

/// <summary>
/// The decisions of <see cref="ExtensionRedirectMiddleware"/>. Umbraco does not route request paths whose last segment
/// has a file extension ("/about/team.html"): they end as a 404 before its redirect lookup runs, so the middleware
/// looks those up itself, with the same route and culture as Umbraco's ContentFinderByRedirectUrl.
/// </summary>
public static partial class ExtensionRedirectRules
{
    // Paths Umbraco and its packages serve themselves; compared as whole leading segments, case-insensitively.
    private static readonly string[] FixedReserved = ["/umbraco", "/umbraco-package", "/app_plugins", "/media", "/install", "/mini-profiler-resources", "/.well-known"];

    // A file name, a dot and 1-5 letters or digits ("team.html", "page.aspx"); not "/.env" or "/a.toolongext".
    [GeneratedRegex(@"^[^/]+\.[a-zA-Z0-9]{1,5}$")]
    private static partial Regex FileNameRegex();

    /// <summary>True when the last segment of the path has a file extension. A trailing slash means no file name.</summary>
    public static bool HasExtension(string path)
    {
        var index = path.LastIndexOf('/');
        return FileNameRegex().IsMatch(index >= 0 ? path[(index + 1)..] : path);
    }

    /// <summary>
    /// Reads Umbraco's comma-separated reserved path and URL settings (GlobalSettings.ReservedPaths/ReservedUrls,
    /// "~/app_plugins/,~/install/") into normalized path prefixes ("/app_plugins", "/install").
    /// </summary>
    public static IReadOnlyList<string> ParseReserved(params string?[] settings) =>
        settings
            .SelectMany(setting => (setting ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => UrlNormalizer.RoutePath(value.TrimStart('~')))
            .OfType<string>()
            .Where(prefix => prefix != "/")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>True when the path is (under) a path Umbraco reserves: the fixed list or <paramref name="extraReserved"/>.</summary>
    public static bool IsReserved(string path, IReadOnlyList<string> extraReserved)
    {
        var routePath = UrlNormalizer.RoutePath(path) ?? path;
        return FixedReserved.Concat(extraReserved).Any(prefix => IsUnder(routePath, prefix));
    }

    /// <summary>True for GET and HEAD requests whose path has a file extension and is not reserved.</summary>
    public static bool IsCandidate(string method, string path, IReadOnlyList<string> extraReserved) =>
        (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        && HasExtension(path)
        && !IsReserved(path, extraReserved);

    /// <summary>
    /// The site and culture of a request host ("klant.nl" or "klant.nl:443") and route path. A domain with a path
    /// prefix ("klant.nl/en") only matches paths under that prefix, the longest prefix first; otherwise the first domain
    /// without a prefix on the host wins. An unknown host belongs to the first site without domains, which Umbraco
    /// serves on any host; null when there is none.
    /// </summary>
    public static SiteMatch? MatchSite(IReadOnlyList<SiteInfo> sites, string host, string path)
    {
        var hostName = HostName(host);
        if (hostName is null) return null;

        var candidates = sites
            .SelectMany(site => site.DomainsByCulture
                .Where(domain => domain.Value.Contains(hostName, StringComparer.OrdinalIgnoreCase))
                .Select(domain => (Site: site, Culture: domain.Key, Prefixes: PrefixesOf(site, domain.Key))))
            .ToList();

        var prefixed = candidates
            .SelectMany(c => c.Prefixes.Where(prefix => IsUnder(path, prefix)).Select(prefix => (c.Site, c.Culture, Prefix: prefix)))
            .OrderByDescending(c => c.Prefix.Length)
            .FirstOrDefault();
        if (prefixed.Site is not null) return new SiteMatch(prefixed.Site, prefixed.Culture);

        var plain = candidates.FirstOrDefault(c => c.Prefixes.Count == 0);
        if (plain.Site is not null) return new SiteMatch(plain.Site, plain.Culture);

        if (candidates.Count > 0) return null;

        var withoutDomains = sites.FirstOrDefault(site => site.Domains.Count == 0 && site.Cultures.Count > 0);
        return withoutDomains is null ? null : new SiteMatch(withoutDomains, withoutDomains.Cultures[0]);
    }

    /// <summary>
    /// The redirect lookup for a request path (as requested, percent-encoded or not): the route
    /// (<see cref="RedirectRoute.For"/> of <see cref="UrlNormalizer.RoutePath"/>) and culture of the matched site.
    /// Null when the path is invalid or the host matches no site.
    /// </summary>
    public static ExtensionRedirectLookup? Lookup(IReadOnlyList<SiteInfo> sites, string host, string requestPath)
    {
        if (UrlNormalizer.RoutePath(requestPath) is not { } path) return null;
        if (MatchSite(sites, host, path) is not { } match) return null;
        return new ExtensionRedirectLookup(match.Site, match.Culture, RedirectRoute.For(match.Site, match.Culture, path));
    }

    /// <summary>Appends the request's query string ("?a=1", or empty) to the redirect target.</summary>
    public static string WithQuery(string target, string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?") return target;
        var parameters = query.StartsWith('?') ? query[1..] : query;
        return target + (target.Contains('?', StringComparison.Ordinal) ? "&" : "?") + parameters;
    }

    private static string? HostName(string host)
    {
        var value = host.Trim();
        if (value.Length == 0) return null;
        return Uri.TryCreate("http://" + value, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host.ToLowerInvariant() : null;
    }

    private static IReadOnlyList<string> PrefixesOf(SiteInfo site, string culture)
    {
        if (site.PathPrefixesByCulture is not { } byCulture) return [];
        return byCulture.TryGetValue(culture, out var prefixes)
            ? prefixes
            : byCulture.FirstOrDefault(p => string.Equals(p.Key, culture, StringComparison.OrdinalIgnoreCase)).Value ?? [];
    }

    private static bool IsUnder(string path, string prefix) =>
        string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
