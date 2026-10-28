namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>
/// A redirect in Umbraco's redirect list: its key, target page and creation date (UTC). <see cref="AllCultures"/> is
/// true for a culture-less redirect (Umbraco's own tracking writes those for invariant pages), which serves every
/// culture that has no redirect of its own for the route.
/// </summary>
public record ExistingRedirect(Guid Key, Guid PageKey, DateTime CreateDateUtc, bool AllCultures);

/// <summary>Umbraco's redirect list for one route (see <see cref="RedirectRoute"/>) and culture.</summary>
public interface IRedirectStore
{
    /// <summary>
    /// The redirect in effect for the route and culture, as Umbraco resolves it: the newest redirect of the culture
    /// (compared case-insensitively), else the newest culture-less one. Empty when neither exists.
    /// </summary>
    IReadOnlyList<ExistingRedirect> Find(string route, string culture);

    void Add(string route, Guid pageKey, string culture);

    /// <summary>
    /// Deletes every redirect of the route in exactly this culture. Culture-less redirects serve all languages and
    /// are kept; a redirect of the culture overrides them.
    /// </summary>
    void RemoveExact(string route, string culture);
}
