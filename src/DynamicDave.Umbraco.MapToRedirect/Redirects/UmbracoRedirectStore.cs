using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

// Register and Delete(Guid) are obsolete in Umbraco 18 (removal planned for 20) in favour of RegisterWithStatus and
// DeleteWithStatus, which do not exist in 17.
#pragma warning disable CS0618

/// <summary>
/// <see cref="IRedirectStore"/> over Umbraco's <see cref="IRedirectUrlService"/>, using only Register,
/// GetMostRecentRedirectUrl(url, culture) and Delete(Guid), which are identical in Umbraco 17 and 18.
/// </summary>
/// <remarks>
/// GetMostRecentRedirectUrl(url, culture) returns the newest redirect of that culture (compared case-insensitively)
/// and only when there is none the newest culture-less one (culture null or ""; Umbraco's own tracking writes "" for
/// invariant pages). That fallback is filtered out here. The service has no lookup that lists every row of a route,
/// so <see cref="Find"/> returns the redirect in effect only; older rows of the same route and culture are shadowed
/// by it and never redirect.
/// </remarks>
public sealed class UmbracoRedirectStore(IRedirectUrlService redirectUrlService) : IRedirectStore
{
    // A route rarely has more than a few rows; the bound only guards against a service that does not delete.
    private const int MaxDeletes = 1000;

    public IReadOnlyList<ExistingRedirect> Find(string route, string culture) =>
        MostRecent(route, culture) is { } redirect
            ? [new ExistingRedirect(redirect.Key, redirect.ContentKey, redirect.CreateDateUtc)]
            : [];

    // The culture is stored as given: Umbraco's own tracking writes the ISO code ("nl-NL") for variant pages.
    public void Add(string route, Guid pageKey, string culture) => redirectUrlService.Register(route, pageKey, culture);

    public void RemoveAll(string route, string culture)
    {
        for (var i = 0; i < MaxDeletes && MostRecent(route, culture) is { } redirect; i++)
        {
            redirectUrlService.Delete(redirect.Key);
        }
    }

    private IRedirectUrl? MostRecent(string route, string culture) =>
        redirectUrlService.GetMostRecentRedirectUrl(route, culture) is { } redirect
        && string.Equals(redirect.Url, route, StringComparison.Ordinal)
        && string.Equals(redirect.Culture, culture, StringComparison.OrdinalIgnoreCase)
            ? redirect
            : null;
}
#pragma warning restore CS0618
