namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>
/// A site: a root content node with its host names (lower-case, empty when the site has no domains) and its
/// cultures (ISO codes of its domains, or the default language when it has none).
/// </summary>
public record SiteInfo(Guid RootKey, int RootId, string Name, IReadOnlyList<string> Domains, IReadOnlyList<string> Cultures);
