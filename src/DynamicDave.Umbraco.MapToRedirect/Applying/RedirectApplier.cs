using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using Umbraco.Cms.Infrastructure.Scoping;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

/// <summary>The outcome of an apply: the import's key and counts, and every row as written to the history.</summary>
public sealed record ApplyResult(Guid ImportKey, ImportCounts Counts, IReadOnlyList<ImportRowRecord> Rows);

/// <summary>
/// Applies a test run to Umbraco's redirect list (spec 4.5). Every Add, Conflict and AlreadyPresent row is checked
/// again against the current state first; a row whose state changed since the test run is not executed and gets the
/// outcome <see cref="ApplyOutcome.Changed"/>. Each row's redirect change and its history row share one scope.
/// </summary>
public sealed class RedirectApplier(IRedirectStore store, IImportHistory history, ISiteCatalog catalog, IScopeProvider scopeProvider, TimeProvider timeProvider)
{
    /// <summary>Length of the user and file name columns.</summary>
    public const int MaxNameLength = 255;

    /// <summary>Length of the culture columns.</summary>
    public const int MaxCultureLength = 20;

    /// <param name="onDone">Called after each row with the number of rows done.</param>
    /// <remarks>
    /// When a row throws, its change is rolled back, the import is completed as <see cref="ImportState.Partial"/>
    /// with the rows done so far, and the exception is rethrown.
    /// </remarks>
    public ApplyResult Apply(RedirectPlan plan, bool overwrite, Guid userKey, string userName, Action<int> onDone)
    {
        var site = catalog.GetSite(plan.RootKey);
        var pages = site is null ? [] : catalog.GetPages(site, plan.Culture);
        var culture = Cut(plan.Culture, MaxCultureLength);

        var importKey = Guid.NewGuid();
        var importId = history.Create(new ImportRecord(
            importKey,
            timeProvider.GetUtcNow().UtcDateTime,
            userKey,
            Cut(userName, MaxNameLength),
            Cut(plan.FileName, MaxNameLength),
            plan.RootKey,
            culture,
            overwrite,
            new ImportCounts(0, 0, 0, 0, 0),
            ImportState.Applied,
            null,
            null));

        var rows = new List<ImportRowRecord>(plan.Rows.Count);
        try
        {
            foreach (var planRow in plan.Rows)
            {
                using (var scope = scopeProvider.CreateScope())
                {
                    var row = Execute(planRow, plan.Culture, overwrite, site, pages) with { Culture = culture };
                    history.AddRow(importId, row);
                    scope.Complete();
                    rows.Add(row);
                }

                onDone(rows.Count);
            }
        }
        catch
        {
            history.Complete(importId, Count(rows), ImportState.Partial);
            throw;
        }

        var counts = Count(rows);
        history.Complete(importId, counts, ImportState.Applied);
        return new ApplyResult(importKey, counts, rows);
    }

    private ImportRowRecord Execute(PlanRow row, string culture, bool overwrite, SiteInfo? site, IReadOnlyList<SitePage> pages)
    {
        var record = new ImportRowRecord(row.RowNumber, row.Route ?? string.Empty, culture, ApplyOutcome.Skipped, row.PageKey, null, null,
            row.OldUrl, row.NewUrl, null);

        switch (row.Outcome)
        {
            case PlanOutcome.Skip:
                return record with { Message = SkipMessage(row.Reason) };
            case PlanOutcome.Error:
                return record with { Outcome = ApplyOutcome.Failed, Message = ErrorMessage(row.Reason) };
        }

        var route = row.Route!;
        var pageKey = row.PageKey!.Value;

        if (site is null) return Changed(record, "The site no longer exists.");

        if (catalog.Resolve(site, culture, pages, pageKey.ToString()) is null)
        {
            return Changed(record, "The target page is no longer published.");
        }

        if (catalog.IsPublishedPath(pages, row.OldPath!))
        {
            return Changed(record, "A published page now exists at the old URL.");
        }

        var current = store.Find(route, culture).FirstOrDefault();
        if (current?.PageKey == pageKey) return record with { Outcome = ApplyOutcome.AlreadyPresent };

        switch (row.Outcome)
        {
            case PlanOutcome.Add when current is null:
                store.Add(route, pageKey, culture);
                return record with { Outcome = ApplyOutcome.Added };

            case PlanOutcome.Conflict when current is not null
                                           && current.PageKey == row.CurrentPageKey
                                           && current.AllCultures == row.CurrentAllCultures:
                if (!overwrite) return record with { Message = "An existing redirect to another page was kept." };

                // A culture-less redirect serves all languages: it is kept, and ours overrides it for this culture.
                if (!current.AllCultures) store.RemoveExact(route, culture);
                store.Add(route, pageKey, culture);
                return record with
                {
                    Outcome = ApplyOutcome.Replaced,
                    ReplacedPageKey = current.PageKey,
                    ReplacedCreateDateUtc = current.CreateDateUtc,
                    ReplacedAllCultures = current.AllCultures,
                    Message = current.AllCultures ? "Overrides a redirect for all languages, which was kept." : null,
                };

            default:
                return Changed(record, current is null
                    ? "The existing redirect was removed since the test run."
                    : "The old URL redirects to another page since the test run.");
        }
    }

    private static ImportRowRecord Changed(ImportRowRecord record, string message) =>
        record with { Outcome = ApplyOutcome.Changed, Message = message };

    private static string SkipMessage(PlanReason reason) => reason switch
    {
        PlanReason.PageExistsAtPath => "A published page exists at the old URL.",
        PlanReason.EmptyNewUrl => "The new URL is empty.",
        PlanReason.SameAsTarget => "The old URL is the URL of the target page.",
        PlanReason.Duplicate => "The old URL appears earlier in the file.",
        _ => "Skipped.",
    };

    private static string ErrorMessage(PlanReason reason) => reason switch
    {
        PlanReason.InvalidOldUrl => "The old URL is not valid.",
        PlanReason.InvalidNewUrl => "The new URL is not valid.",
        PlanReason.NotPublished => "The new URL is not a published page of the site.",
        PlanReason.OtherSite => "The new URL belongs to another site or language.",
        _ => "Error.",
    };

    // Changed rows changed nothing, like skipped ones.
    private static ImportCounts Count(IReadOnlyList<ImportRowRecord> rows) => new(
        rows.Count(r => r.Outcome == ApplyOutcome.Added),
        rows.Count(r => r.Outcome == ApplyOutcome.Replaced),
        rows.Count(r => r.Outcome == ApplyOutcome.AlreadyPresent),
        rows.Count(r => r.Outcome is ApplyOutcome.Skipped or ApplyOutcome.Changed),
        rows.Count(r => r.Outcome == ApplyOutcome.Failed));

    internal static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}
