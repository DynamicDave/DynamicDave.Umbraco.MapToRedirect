namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>A redirect in Umbraco's redirect list: its key, target page and creation date (UTC).</summary>
public record ExistingRedirect(Guid Key, Guid PageKey, DateTime CreateDateUtc);

/// <summary>
/// Umbraco's redirect list for one route (see <see cref="RedirectRoute"/>) and culture. Routes and cultures are
/// matched exactly (cultures case-insensitively); culture-less redirects do not belong to any culture here.
/// </summary>
public interface IRedirectStore
{
    /// <summary>The redirects for the route and culture, newest first. The first one is the redirect in effect.</summary>
    IReadOnlyList<ExistingRedirect> Find(string route, string culture);

    void Add(string route, Guid pageKey, string culture);

    void RemoveAll(string route, string culture);
}
