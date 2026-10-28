using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

/// <summary>The outcome of an apply: the import's key and counts, and every row as written to the history.</summary>
public sealed record ApplyResult(Guid ImportKey, ImportCounts Counts, IReadOnlyList<ImportRowRecord> Rows);

/// <summary>
/// Applies a test run to Umbraco's redirect list (spec 4.5). Every Add, Conflict and AlreadyPresent row is checked
/// again against the current state first; a row whose state changed since the test run is not executed and gets the
/// outcome <see cref="ApplyOutcome.Changed"/>. Each row's redirect change and its history row share one scope.
/// </summary>
public sealed class RedirectApplier(
    IRedirectStore store,
    IImportHistory history,
    ISiteCatalog catalog,
    IScopeProvider scopeProvider,
    TimeProvider timeProvider,
    ILogger<RedirectApplier> logger)
{
    /// <summary>Length of the user and file name columns.</summary>
    public const int MaxNameLength = 255;

    /// <summary>Length of the culture columns.</summary>
    public const int MaxCultureLength = 20;

    /// <param name="onDone">Called after each row with the number of rows done.</param>
    /// <remarks>
    /// The import is created as <see cref="ImportState.Partial"/> and completed as <see cref="ImportState.Applied"/>
    /// at the end, so a process that dies midway leaves an honest state. When a row throws, its change is rolled
    /// back, the counts of the rows done so far are stored (state Partial), and the exception is rethrown.
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
            new ImportCounts(0, 0, 0, 0, 0, 0),
            ImportState.Partial,
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
            // Must not hide the exception that stopped the loop.
            try
            {
                history.Complete(importId, Count(rows), ImportState.Partial);
            }
            catch (Exception completeError)
            {
                logger.LogError(completeError, "Could not store the counts of partial import {ImportKey}.", importKey);
            }

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
                return WithMessage(record, SkipCode(row.Reason));
            case PlanOutcome.Error:
                return WithMessage(record with { Outcome = ApplyOutcome.Failed }, ErrorCode(row.Reason));
        }

        var route = row.Route!;
        var pageKey = row.PageKey!.Value;

        if (site is null) return Changed(record, RowMessages.SiteRemoved);

        if (catalog.Resolve(site, culture, pages, pageKey.ToString()) is null)
        {
            return Changed(record, RowMessages.TargetUnpublished);
        }

        if (catalog.IsPublishedPath(pages, row.OldPath!))
        {
            return Changed(record, RowMessages.PageAtOldUrl);
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
                if (!overwrite) return WithMessage(record, RowMessages.ConflictKept);

                // A culture-less redirect serves all languages: it is kept, and ours overrides it for this culture.
                if (!current.AllCultures) store.RemoveExact(route, culture);
                store.Add(route, pageKey, culture);
                var replaced = record with
                {
                    Outcome = ApplyOutcome.Replaced,
                    ReplacedPageKey = current.PageKey,
                    ReplacedCreateDateUtc = current.CreateDateUtc,
                    ReplacedAllCultures = current.AllCultures,
                };
                return current.AllCultures ? WithMessage(replaced, RowMessages.OverridesAllLanguages) : replaced;

            default:
                return Changed(record, current is null ? RowMessages.RedirectRemoved : RowMessages.RedirectChanged);
        }
    }

    private static ImportRowRecord Changed(ImportRowRecord record, string code) => WithMessage(record with { Outcome = ApplyOutcome.Changed }, code);

    // The English text goes to the report CSV; the dashboard localizes the code.
    private static ImportRowRecord WithMessage(ImportRowRecord record, string code) =>
        record with { MessageCode = code, Message = RowMessages.English(code) };

    private static string SkipCode(PlanReason reason) => reason switch
    {
        PlanReason.PageExistsAtPath or PlanReason.EmptyNewUrl or PlanReason.SameAsTarget or PlanReason.Duplicate => reason.ToString(),
        _ => RowMessages.Skipped,
    };

    private static string ErrorCode(PlanReason reason) => reason switch
    {
        PlanReason.InvalidOldUrl or PlanReason.InvalidNewUrl or PlanReason.NotPublished or PlanReason.OtherSite => reason.ToString(),
        _ => RowMessages.Error,
    };

    private static ImportCounts Count(IReadOnlyList<ImportRowRecord> rows) => new(
        rows.Count(r => r.Outcome == ApplyOutcome.Added),
        rows.Count(r => r.Outcome == ApplyOutcome.Replaced),
        rows.Count(r => r.Outcome == ApplyOutcome.AlreadyPresent),
        rows.Count(r => r.Outcome == ApplyOutcome.Skipped),
        rows.Count(r => r.Outcome == ApplyOutcome.Failed),
        rows.Count(r => r.Outcome == ApplyOutcome.Changed));

    internal static string Cut(string value, int length) => value.Length > length ? value[..length] : value;
}
