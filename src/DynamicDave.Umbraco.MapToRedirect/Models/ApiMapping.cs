using System.Text;
using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Models;

/// <summary>Request validation and the mapping between the package's records and the API models.</summary>
public static class ApiMapping
{
    public const int MaxSitemapUrlLength = 2048;

    // No preamble: the stored text already starts with U+FEFF.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The error code when the site does not exist or the culture is not one of its cultures (case-insensitive);
    /// otherwise the culture as the site spells it.
    /// </summary>
    public static (string? Error, string? Culture) ValidateSite(SiteInfo? site, string? culture)
    {
        if (site is null) return (ApiErrors.SiteNotFound, null);

        var match = site.Cultures.FirstOrDefault(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));
        return match is null ? (ApiErrors.CultureNotFound, null) : (null, match);
    }

    /// <summary>The error code of an invalid sitemap URL or method. The site and culture are checked by <see cref="ValidateSite"/>.</summary>
    public static string? ValidateGenerate(GenerateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SitemapUrl) || request.SitemapUrl.Length > MaxSitemapUrlLength) return ApiErrors.SitemapUrlInvalid;
        return Enum.IsDefined(request.Method) ? null : ApiErrors.MethodInvalid;
    }

    public static PlanResponse ToPlanResponse(RedirectPlan plan)
    {
        var counts = plan.Rows.GroupBy(r => r.Outcome).ToDictionary(g => g.Key, g => g.Count());
        return new PlanResponse
        {
            PlanId = plan.Id,
            FileName = plan.FileName,
            Counts = Enum.GetValues<PlanOutcome>()
                .Select(o => new OutcomeCountModel { Outcome = o, Count = counts.GetValueOrDefault(o) })
                .ToArray(),
            FirstPage = ToPlanRows(plan, null, 0, Paging.FirstPageSize),
        };
    }

    /// <summary>The rows with one of <paramref name="outcomes"/> (all rows when null or empty), in row order.</summary>
    public static PlanRowsResponse ToPlanRows(RedirectPlan plan, PlanOutcome[]? outcomes, int skip, int take)
    {
        var rows = outcomes is { Length: > 0 } ? plan.Rows.Where(r => outcomes.Contains(r.Outcome)).ToList() : plan.Rows;
        return new PlanRowsResponse { Total = rows.Count, Items = rows.Skip(skip).Take(take).Select(ToModel).ToArray() };
    }

    public static PlanRowModel ToModel(PlanRow row) => new()
    {
        RowNumber = row.RowNumber,
        OldUrl = row.OldUrl,
        NewUrl = row.NewUrl,
        OldPath = row.OldPath,
        Route = row.Route,
        PageKey = row.PageKey,
        PagePath = row.PagePath,
        Outcome = row.Outcome,
        Reason = row.Reason,
        CurrentPageKey = row.CurrentPageKey,
        CurrentPagePath = row.CurrentPagePath,
        CurrentAllCultures = row.CurrentAllCultures,
    };

    public static ImportModel ToModel(ImportRecord record) => new()
    {
        Key = record.Key,
        CreatedUtc = record.CreatedUtc,
        UserKey = record.UserKey,
        UserName = record.UserName,
        FileName = record.FileName,
        RootKey = record.RootKey,
        Culture = record.Culture,
        Overwrite = record.Overwrite,
        Counts = new ImportCountsModel
        {
            Added = record.Counts.Added,
            Replaced = record.Counts.Replaced,
            AlreadyPresent = record.Counts.AlreadyPresent,
            Skipped = record.Counts.Skipped,
            Failed = record.Counts.Failed,
            Changed = record.Counts.Changed,
        },
        State = record.State,
        UndoneUtc = record.UndoneUtc,
        UndoneBy = record.UndoneBy,
    };

    public static ImportRowModel ToModel(ImportRowRecord row) => new()
    {
        RowNumber = row.RowNumber,
        OldRoute = row.OldRoute,
        Culture = row.Culture,
        Outcome = row.Outcome,
        NewPageKey = row.NewPageKey,
        ReplacedPageKey = row.ReplacedPageKey,
        ReplacedCreateDateUtc = row.ReplacedCreateDateUtc,
        OldUrl = row.OldUrl,
        NewUrl = row.NewUrl,
        Message = row.Message,
        MessageCode = row.MessageCode,
        ReplacedAllCultures = row.ReplacedAllCultures,
        Undone = row.Undone,
    };

    public static JobStateModel ToModel(JobState state) => new()
    {
        Id = state.Id,
        Kind = state.Kind,
        Status = state.Status,
        UserKey = state.UserKey,
        UserName = state.UserName,
        StartedUtc = state.StartedUtc,
        HeartbeatUtc = state.HeartbeatUtc,
        CompletedUtc = state.CompletedUtc,
        Done = state.Done,
        Total = state.Total,
        Phase = state.Phase,
        ResultKey = state.ResultKey,
        ResultKind = state.ResultKind,
        Error = state.Error,
        Summary = state.Summary,
    };

    public static SiteModel ToModel(SiteInfo site) => new()
    {
        RootKey = site.RootKey,
        Name = site.Name,
        Domains = [.. site.Domains],
        Cultures = [.. site.Cultures],
    };

    /// <summary>A stored CSV as UTF-8 bytes: its leading U+FEFF becomes the one BOM.</summary>
    public static byte[] DownloadBytes(string content) => Utf8.GetBytes(content);

    public static string UploadFileName(string fileName) =>
        fileName.Length > RedirectPlanner.MaxFileNameLength ? fileName[..RedirectPlanner.MaxFileNameLength] : fileName;
}
