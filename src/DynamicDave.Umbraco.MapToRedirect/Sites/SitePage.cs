namespace DynamicDave.Umbraco.MapToRedirect.Sites;

/// <summary>
/// A published page of a site in one culture. <see cref="Path"/> is the normalized relative URL path as Umbraco
/// renders it in that culture: it includes a domain path prefix (for a domain "klant.nl/en" the root is "/en" and a
/// child "/en/about"); without such a prefix the root is "/". <see cref="Slug"/> is its last segment,
/// <see cref="Title"/> the value of a title-like property when the page has one.
/// </summary>
public record SitePage(Guid Key, string Path, string Slug, string Name, string? Title);
