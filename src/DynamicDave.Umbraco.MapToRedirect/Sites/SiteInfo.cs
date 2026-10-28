namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>
/// A site: a root content node with its host names (lower-case, distinct, empty when the site has no domains; for
/// display), its cultures (ISO codes of its domains including wildcard domains, or the default language when it has
/// none) and the host names per culture (culture keys compared case-insensitively; wildcard domains have no host).
/// <paramref name="CultureDomains"/> holds each culture's domains as host and normalized path prefix ("klant.nl/en"
/// gives host "klant.nl" with prefix "/en", "klant.nl" an empty prefix); null means the hosts of
/// <paramref name="DomainsByCulture"/> without prefixes.
/// </summary>
public record SiteInfo(
    Guid RootKey,
    int RootId,
    string Name,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Cultures,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DomainsByCulture,
    IReadOnlyDictionary<string, IReadOnlyList<CultureDomain>>? CultureDomains = null)
{
    /// <summary>The domains (host and path prefix) of the culture, compared case-insensitively; empty when it has none.</summary>
    public IReadOnlyList<CultureDomain> DomainsOf(string culture)
    {
        if (CultureDomains is { } byCulture) return ValueOf(byCulture, culture) ?? [];
        return ValueOf(DomainsByCulture, culture)?.Select(host => new CultureDomain(host, string.Empty)).ToArray() ?? [];
    }

    // The dictionaries are built case-insensitive, but a hand-built SiteInfo may use any comparer.
    private static T? ValueOf<T>(IReadOnlyDictionary<string, T> byCulture, string culture)
        where T : class =>
        byCulture.TryGetValue(culture, out var value)
            ? value
            : byCulture.FirstOrDefault(p => string.Equals(p.Key, culture, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>A domain of a culture: its lower-case host and its normalized path prefix ("/en"; empty when it has none).</summary>
public sealed record CultureDomain(string Host, string PathPrefix);
