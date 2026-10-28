namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

/// <summary>A row of Umbraco's redirect list. <see cref="Culture"/> is "" for a culture-less redirect.</summary>
public record StoredRedirect(Guid Key, string Route, string Culture, Guid PageKey, DateTime CreateDateUtc);

/// <summary>
/// Resolves the redirect in effect for a route and culture from a loaded redirect list, the way Umbraco's
/// GetMostRecentRedirectUrl(url, culture) does: routes compared case-insensitively; the newest redirect of the
/// culture (compared case-insensitively) wins, and only when there is none the newest culture-less one (culture null,
/// empty or whitespace).
/// </summary>
public sealed class EffectiveRedirects
{
    private readonly Dictionary<string, List<StoredRedirect>> _byRoute = new(StringComparer.OrdinalIgnoreCase);

    public EffectiveRedirects(IEnumerable<StoredRedirect> redirects)
    {
        foreach (var redirect in redirects)
        {
            if (!_byRoute.TryGetValue(redirect.Route, out var list)) _byRoute[redirect.Route] = list = [];
            list.Add(redirect);
        }
    }

    public ExistingRedirect? Find(string route, string culture) =>
        _byRoute.TryGetValue(route, out var candidates) ? Select(candidates, culture) : null;

    /// <summary>The redirect in effect among <paramref name="candidates"/> (all for the same route) for the culture.</summary>
    internal static ExistingRedirect? Select(IEnumerable<StoredRedirect> candidates, string culture)
    {
        StoredRedirect? exact = null;
        StoredRedirect? cultureLess = null;
        foreach (var candidate in candidates)
        {
            if (IsCultureLess(candidate.Culture))
            {
                if (cultureLess is null || candidate.CreateDateUtc > cultureLess.CreateDateUtc) cultureLess = candidate;
            }
            else if (!IsCultureLess(culture) && string.Equals(candidate.Culture.Trim(), culture.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (exact is null || candidate.CreateDateUtc > exact.CreateDateUtc) exact = candidate;
            }
        }

        var chosen = exact ?? cultureLess;
        return chosen is null ? null : new ExistingRedirect(chosen.Key, chosen.PageKey, chosen.CreateDateUtc, exact is null);
    }

    internal static bool IsCultureLess(string? culture) => string.IsNullOrWhiteSpace(culture);
}
