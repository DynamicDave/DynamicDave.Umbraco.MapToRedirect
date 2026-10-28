using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Planning;

/// <summary>The test run: one outcome per CSV row (spec 4.4). Reads the redirect list once per plan, never changes it.</summary>
public sealed class RedirectPlanner(IRedirectStore store)
{
    /// <summary>Length of the import file name column.</summary>
    public const int MaxFileNameLength = 255;

    /// <param name="pages">The site's published pages in <paramref name="culture"/> (ISiteCatalog.GetPages).</param>
    public RedirectPlan Plan(Guid id, string fileName, IReadOnlyList<CsvRow> rows, SiteInfo site, string culture, IReadOnlyList<SitePage> pages)
    {
        var byKey = new Dictionary<Guid, SitePage>();
        foreach (var page in pages) byKey.TryAdd(page.Key, page);

        var existing = new EffectiveRedirects(store.LoadAll());
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planned = new List<PlanRow>(rows.Count);
        foreach (var row in rows)
        {
            planned.Add(PlanRow(row, site, culture, pages, byKey, seen, existing));
        }

        var name = fileName.Length > MaxFileNameLength ? fileName[..MaxFileNameLength] : fileName;
        return new RedirectPlan(id, site.RootKey, culture, name, planned);
    }

    private static PlanRow PlanRow(
        CsvRow row,
        SiteInfo site,
        string culture,
        IReadOnlyList<SitePage> pages,
        Dictionary<Guid, SitePage> byKey,
        HashSet<string> seen,
        EffectiveRedirects existing)
    {
        var result = new PlanRow(row.RowNumber, row.OldUrl, row.NewUrl, null, null, null, null, PlanOutcome.Error, PlanReason.InvalidOldUrl, null, null, false);

        // The path as requested (extensions kept): Umbraco looks redirects up by the requested path.
        var oldPath = UrlNormalizer.RoutePath(row.OldUrl);
        if (oldPath is null) return result;

        // Null when every domain of the culture has a path prefix and the old path is under none of them: no request in
        // the culture reaches that path, so a redirect for it would never fire.
        result = result with { OldPath = oldPath, Route = RedirectRoute.For(site, culture, oldPath) };
        if (result.Route is null) return Outcome(result, PlanOutcome.Error, PlanReason.OutsideLanguagePath);

        // The first row of a route wins, whatever its outcome. Different old paths can share a route ("/en/x" and
        // "/english/x" on domains with path prefixes "/en" and "/english").
        if (!seen.Add(result.Route)) return Outcome(result, PlanOutcome.Skip, PlanReason.Duplicate);

        var newUrl = row.NewUrl.Trim();
        if (newUrl.Length == 0) return Outcome(result, PlanOutcome.Skip, PlanReason.EmptyNewUrl);

        var isKey = Guid.TryParse(newUrl, out _);
        if (!isKey && UrlNormalizer.HostOf(newUrl) is not null && !SiteCatalogRules.HostBelongsTo(site, culture, newUrl))
        {
            return Outcome(result, PlanOutcome.Error, PlanReason.OtherSite);
        }

        if (!isKey && UrlNormalizer.Normalize(newUrl) is null) return Outcome(result, PlanOutcome.Error, PlanReason.InvalidNewUrl);

        if (SiteCatalogRules.Resolve(site, culture, pages, newUrl) is not { } target)
        {
            return Outcome(result, PlanOutcome.Error, PlanReason.NotPublished);
        }

        result = result with { PageKey = target.Key, PagePath = target.Path };

        // Umbraco only redirects when no published page exists at the requested path.
        if (SiteCatalogRules.IsPublishedPath(pages, oldPath)) return Outcome(result, PlanOutcome.Skip, PlanReason.PageExistsAtPath);

        if (string.Equals(UrlNormalizer.RoutePath(target.Path), oldPath, StringComparison.Ordinal))
        {
            return Outcome(result, PlanOutcome.Skip, PlanReason.SameAsTarget);
        }

        // The redirect in effect may be a culture-less one (all languages); ours would override it for this culture.
        var current = existing.Find(result.Route!, culture);
        if (current is null) return Outcome(result, PlanOutcome.Add, PlanReason.None);
        if (current.PageKey == target.Key) return Outcome(result, PlanOutcome.AlreadyPresent, PlanReason.None);

        return Outcome(result, PlanOutcome.Conflict, PlanReason.None) with
        {
            CurrentPageKey = current.PageKey,
            CurrentPagePath = byKey.GetValueOrDefault(current.PageKey)?.Path,
            CurrentAllCultures = current.AllCultures,
        };
    }

    private static PlanRow Outcome(PlanRow row, PlanOutcome outcome, PlanReason reason) => row with { Outcome = outcome, Reason = reason };
}
