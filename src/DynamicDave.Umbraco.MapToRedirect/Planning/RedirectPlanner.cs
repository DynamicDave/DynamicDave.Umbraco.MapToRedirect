using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Planning;

/// <summary>The test run: one outcome per CSV row (spec 4.4). Reads the redirect list, never changes it.</summary>
public sealed class RedirectPlanner(IRedirectStore store)
{
    /// <summary>Length of the import file name column.</summary>
    public const int MaxFileNameLength = 255;

    /// <param name="pages">The site's published pages in <paramref name="culture"/> (ISiteCatalog.GetPages).</param>
    public RedirectPlan Plan(Guid id, string fileName, IReadOnlyList<CsvRow> rows, SiteInfo site, string culture, IReadOnlyList<SitePage> pages)
    {
        var byKey = new Dictionary<Guid, SitePage>();
        foreach (var page in pages) byKey.TryAdd(page.Key, page);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var planned = new List<PlanRow>(rows.Count);
        foreach (var row in rows)
        {
            planned.Add(PlanRow(row, site, culture, pages, byKey, seen));
        }

        var name = fileName.Length > MaxFileNameLength ? fileName[..MaxFileNameLength] : fileName;
        return new RedirectPlan(id, site.RootKey, culture, name, planned);
    }

    private PlanRow PlanRow(
        CsvRow row, SiteInfo site, string culture, IReadOnlyList<SitePage> pages, Dictionary<Guid, SitePage> byKey, HashSet<string> seen)
    {
        var result = new PlanRow(row.RowNumber, row.OldUrl, row.NewUrl, null, null, null, null, PlanOutcome.Error, PlanReason.InvalidOldUrl, null, null);

        var oldPath = UrlNormalizer.Normalize(row.OldUrl);
        if (oldPath is null) return result;

        result = result with { OldPath = oldPath, Route = RedirectRoute.For(site, oldPath) };

        // The first row of an old path wins, whatever its outcome.
        if (!seen.Add(oldPath)) return Outcome(result, PlanOutcome.Skip, PlanReason.Duplicate);

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

        if (string.Equals(UrlNormalizer.Normalize(target.Path), oldPath, StringComparison.Ordinal))
        {
            return Outcome(result, PlanOutcome.Skip, PlanReason.SameAsTarget);
        }

        // Umbraco only redirects when no published page exists at the requested path.
        if (SiteCatalogRules.IsPublishedPath(pages, oldPath)) return Outcome(result, PlanOutcome.Skip, PlanReason.PageExistsAtPath);

        var current = store.Find(result.Route!, culture).FirstOrDefault();
        if (current is null) return Outcome(result, PlanOutcome.Add, PlanReason.None);
        if (current.PageKey == target.Key) return Outcome(result, PlanOutcome.AlreadyPresent, PlanReason.None);

        return Outcome(result, PlanOutcome.Conflict, PlanReason.None) with
        {
            CurrentPageKey = current.PageKey,
            CurrentPagePath = byKey.GetValueOrDefault(current.PageKey)?.Path,
        };
    }

    private static PlanRow Outcome(PlanRow row, PlanOutcome outcome, PlanReason reason) => row with { Outcome = outcome, Reason = reason };
}
