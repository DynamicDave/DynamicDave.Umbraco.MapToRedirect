using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.MapToRedirect.Redirects;

// Register and Delete(Guid) are obsolete in Umbraco 18; their *WithStatus replacements do not exist in 17.
#pragma warning disable CS0618

/// <summary>
/// <see cref="IRedirectStore"/> over Umbraco's <see cref="IRedirectUrlService"/>, using only Register,
/// GetMostRecentRedirectUrl(url, culture), GetAllRedirectUrls(pageIndex, pageSize, out total) and Delete(Guid), which
/// are identical in Umbraco 17 and 18.
/// </summary>
/// <remarks>
/// GetMostRecentRedirectUrl(url, culture) is the lookup Umbraco's ContentFinderByRedirectUrl uses: it returns the
/// newest redirect of that culture (compared case-insensitively) and only when there is none the newest culture-less
/// one (culture null or blank; Umbraco's own tracking writes "" for invariant pages). The service has no lookup that
/// lists every row of a route, so <see cref="Find"/> returns the redirect in effect only; older rows of the same route
/// and culture are shadowed by it and never redirect.
/// </remarks>
public sealed class UmbracoRedirectStore(IRedirectUrlService redirectUrlService) : IRedirectStore
{
    // A route rarely has more than a few rows; the bound only guards against a service that does not delete.
    private const int MaxDeletes = 1000;

    private const int PageSize = 1000;

    // Same selection as EffectiveRedirects, applied to the single row the service returns.
    public IReadOnlyList<ExistingRedirect> Find(string route, string culture) =>
        redirectUrlService.GetMostRecentRedirectUrl(route, culture) is { } redirect
        && EffectiveRedirects.Select([ToStored(redirect)], culture) is { } effective
        && string.Equals(redirect.Url, route, StringComparison.OrdinalIgnoreCase)
            ? [effective]
            : [];

    /// <summary>Every redirect of the installation, read in pages of <see cref="PageSize"/>.</summary>
    public IReadOnlyList<StoredRedirect> LoadAll()
    {
        var all = new List<StoredRedirect>();
        for (long page = 0; ; page++)
        {
            var batch = redirectUrlService.GetAllRedirectUrls(page, PageSize, out var total).ToList();
            all.AddRange(batch.Select(ToStored));
            if (batch.Count < PageSize || all.Count >= total) return all;
        }
    }

    // The culture is stored as given: Umbraco's own tracking writes the ISO code ("nl-NL") for variant pages.
    public void Add(string route, Guid pageKey, string culture) => redirectUrlService.Register(route, pageKey, culture);

    // The lookup returns rows of the culture before culture-less ones, so this stops once only culture-less rows (or
    // none) are left.
    public void RemoveExact(string route, string culture)
    {
        for (var i = 0; i < MaxDeletes && Find(route, culture) is [{ AllCultures: false } redirect]; i++)
        {
            redirectUrlService.Delete(redirect.Key);
        }
    }

    private static StoredRedirect ToStored(IRedirectUrl redirect) =>
        new(redirect.Key, redirect.Url, redirect.Culture ?? string.Empty, redirect.ContentKey, redirect.CreateDateUtc);
}
#pragma warning restore CS0618
