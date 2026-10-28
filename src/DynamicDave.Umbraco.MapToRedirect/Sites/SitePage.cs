namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>
/// A published page of a site in one culture. <see cref="Path"/> is the normalized current URL path (relative to the
/// site's domain, "/" for the root), <see cref="Slug"/> its last segment, <see cref="Title"/> the value of a
/// title-like property when the page has one.
/// </summary>
public record SitePage(Guid Key, string Path, string Slug, string Name, string? Title);
