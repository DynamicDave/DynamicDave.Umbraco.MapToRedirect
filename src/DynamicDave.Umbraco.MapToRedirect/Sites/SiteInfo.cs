namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>
/// A site: a root content node with its host names (lower-case, distinct, empty when the site has no domains; for
/// display), its cultures (ISO codes of its domains including wildcard domains, or the default language when it has
/// none) and the host names per culture (culture keys compared case-insensitively; wildcard domains have no host).
/// </summary>
public record SiteInfo(
    Guid RootKey,
    int RootId,
    string Name,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Cultures,
    IReadOnlyDictionary<string, IReadOnlyList<string>> DomainsByCulture);
