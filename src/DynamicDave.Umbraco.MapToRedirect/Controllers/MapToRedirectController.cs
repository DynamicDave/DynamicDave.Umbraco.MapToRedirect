using Asp.Versioning;
using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Models;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Security;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;

namespace DynamicDave.Umbraco.MapToRedirect.Controllers;

/// <summary>
/// The dashboard's API (spec §5). Thin: every action checks <see cref="AccessPolicy"/> first (403), validates its
/// input (400), calls the package's units and maps the result. Generate, apply and undo run as the one background job
/// of the installation; starting one while another runs gives 409 with the running job.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "MapToRedirect")]
public class MapToRedirectController(
    AccessPolicy accessPolicy,
    ISiteCatalog catalog,
    RedirectPlanner planner,
    TempStore tempStore,
    IImportHistory history,
    JobRunner jobs,
    IBackOfficeSecurityAccessor security,
    TimeProvider timeProvider) : DynamicDaveUmbracoMapToRedirectApiControllerBase
{
    private const string CsvContentType = "text/csv; charset=utf-8";

    [HttpGet("access")]
    [ProducesResponseType<AccessResponse>(StatusCodes.Status200OK)]
    public IActionResult GetAccess() => Ok(new AccessResponse { HasAccess = accessPolicy.HasAccess(CurrentUser()) });

    [HttpGet("sites")]
    [ProducesResponseType<SiteModel[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetSites()
    {
        if (!HasAccess(out _)) return Forbidden();

        return Ok(catalog.GetSites().Select(ApiMapping.ToModel).ToArray());
    }

    [HttpPost("generate")]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status409Conflict)]
    public IActionResult Generate([FromBody] GenerateRequest request)
    {
        if (!HasAccess(out var user)) return Forbidden();
        if (ApiMapping.ValidateGenerate(request) is { } invalid) return Error(invalid);

        var (siteError, culture) = ApiMapping.ValidateSite(catalog.GetSite(request.RootKey), request.Culture);
        if (siteError is not null) return Error(siteError);

        var sitemapUrl = request.SitemapUrl.Trim();
        var method = request.Method;
        var rootKey = request.RootKey;
        return Started(jobs.TryStart(JobKind.Generate, user.Key, UserName(user), (services, progress, ct) =>
            services.GetRequiredService<GenerateJob>().RunAsync(sitemapUrl, method, rootKey, culture!, progress, ct)));
    }

    [HttpPost("plans")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult CreatePlan(IFormFile? file, [FromQuery] Guid rootKey, [FromQuery] string? culture)
    {
        if (!HasAccess(out _)) return Forbidden();

        var site = catalog.GetSite(rootKey);
        var (siteError, siteCulture) = ApiMapping.ValidateSite(site, culture);
        if (siteError is not null) return Error(siteError);
        if (file is null) return Error(nameof(CsvReadError.Unreadable));

        CsvReadResult read;
        using (var stream = file.OpenReadStream())
        {
            read = CsvFormat.Read(stream);
        }

        if (read.Error is { } csvError) return Error(csvError.ToString());

        var pages = catalog.GetPages(site!, siteCulture!);
        var plan = planner.Plan(Guid.NewGuid(), ApiMapping.UploadFileName(Path.GetFileName(file.FileName)), read.Rows, site!, siteCulture!, pages);
        tempStore.Put(plan.Id, TempKind.Plan, PlanJson.Serialize(plan), plan.FileName);
        return Ok(ApiMapping.ToPlanResponse(plan));
    }

    [HttpGet("plans/{id:guid}/rows")]
    [ProducesResponseType<PlanRowsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetPlanRows(Guid id, [FromQuery] PlanOutcome[]? outcome = null, [FromQuery] int skip = 0, [FromQuery] int take = Paging.FirstPageSize)
    {
        if (!HasAccess(out _)) return Forbidden();
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);

        // Model binding accepts any number for an enum.
        if (outcome is not null && !outcome.All(o => Enum.IsDefined(o))) return Error(ApiErrors.InvalidFilter);
        if (LoadPlan(id) is not { } plan) return NotFound(new ErrorResponse { Error = ApiErrors.PlanExpired });

        return Ok(ApiMapping.ToPlanRows(plan, outcome, skip, take));
    }

    [HttpPost("plans/{id:guid}/apply")]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status409Conflict)]
    public IActionResult ApplyPlan(Guid id, [FromBody] ApplyRequest request)
    {
        if (!HasAccess(out var user)) return Forbidden();
        if (LoadPlan(id) is not { } plan) return NotFound(new ErrorResponse { Error = ApiErrors.PlanExpired });

        var overwrite = request.Overwrite;
        var userKey = user.Key;
        var userName = UserName(user);
        return Started(jobs.TryStart(JobKind.Apply, userKey, userName, (services, progress, _) =>
        {
            var total = plan.Rows.Count;
            progress.Report(0, total, JobPhases.Applying);
            var result = services.GetRequiredService<RedirectApplier>()
                .Apply(plan, overwrite, userKey, userName, done => progress.Report(done, total, JobPhases.Applying));

            services.GetRequiredService<TempStore>().Put(
                result.ImportKey, TempKind.Report, ApplyReport.Csv(result.Rows), ApplyReport.FileName(timeProvider.GetUtcNow().UtcDateTime));
            return Task.FromResult<(Guid?, TempKind?, string?)>((result.ImportKey, TempKind.Report, ApplyReport.Summary(result.Counts)));
        }));
    }

    /// <summary>The current or last job; 204 when there has been none.</summary>
    [HttpGet("job")]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetJob()
    {
        if (!HasAccess(out _)) return Forbidden();

        return jobs.Current() is { } state ? Ok(ApiMapping.ToModel(state)) : NoContent();
    }

    /// <summary>The generated CSV or the apply report of the current user's completed job.</summary>
    [HttpGet("job/result")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetJobResult()
    {
        if (!HasAccess(out var user)) return Forbidden();

        var state = jobs.Current();
        if (state is not { Status: JobStatus.Completed, ResultKey: { } key, ResultKind: { } kind } || state.UserKey != user.Key) return Forbidden();
        if (tempStore.Get(key, kind) is not { } entry) return NotFound(new ErrorResponse { Error = ApiErrors.ResultNotFound });

        return File(ApiMapping.DownloadBytes(entry.Content), CsvContentType, entry.FileName ?? "result.csv");
    }

    [HttpGet("imports")]
    [ProducesResponseType<ImportsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetImports([FromQuery] int skip = 0, [FromQuery] int take = Paging.FirstPageSize)
    {
        if (!HasAccess(out _)) return Forbidden();
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);

        var (items, total) = history.List(skip, take);
        return Ok(new ImportsResponse { Total = total, Items = items.Select(ApiMapping.ToModel).ToArray() });
    }

    [HttpGet("imports/{key:guid}/rows")]
    [ProducesResponseType<ImportRowsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetImportRows(Guid key, [FromQuery] ApplyOutcome[]? outcome = null, [FromQuery] int skip = 0, [FromQuery] int take = Paging.FirstPageSize)
    {
        if (!HasAccess(out _)) return Forbidden();
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);
        if (outcome is not null && !outcome.All(o => Enum.IsDefined(o))) return Error(ApiErrors.InvalidFilter);
        if (history.Get(key) is null) return NotFound(new ErrorResponse { Error = ApiErrors.ImportNotFound });

        var (items, total) = history.Rows(key, outcome is { Length: > 0 } ? outcome : null, skip, take);
        return Ok(new ImportRowsResponse { Total = total, Items = items.Select(ApiMapping.ToModel).ToArray() });
    }

    /// <summary>Starts an undo job; 409 with <c>{ error: "alreadyUndone" }</c> for an undone import, or with the running job.</summary>
    [HttpPost("imports/{key:guid}/undo")]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status409Conflict)]
    public IActionResult UndoImport(Guid key)
    {
        if (!HasAccess(out var user)) return Forbidden();
        if (history.Get(key) is not { } import) return NotFound(new ErrorResponse { Error = ApiErrors.ImportNotFound });
        if (import.State == ImportState.Undone) return Conflict(new ErrorResponse { Error = ApiErrors.AlreadyUndone });

        var c = import.Counts;
        var total = c.Added + c.Replaced + c.AlreadyPresent + c.Skipped + c.Failed + c.Changed;
        var userName = UserName(user);
        return Started(jobs.TryStart(JobKind.Undo, user.Key, userName, (services, progress, _) =>
        {
            progress.Report(0, total, JobPhases.Undoing);
            var result = services.GetRequiredService<ImportUndoer>()
                .Undo(key, userName, done => progress.Report(done, Math.Max(total, done), JobPhases.Undoing));
            return Task.FromResult<(Guid?, TempKind?, string?)>((null, null, ApplyReport.Summary(result)));
        }));
    }

    private IUser? CurrentUser() => security.BackOfficeSecurity?.CurrentUser;

    private bool HasAccess(out IUser user)
    {
        user = CurrentUser()!;
        return accessPolicy.HasAccess(user);
    }

    private static string UserName(IUser user) => user.Name ?? user.Username;

    private RedirectPlan? LoadPlan(Guid id) => tempStore.Get(id, TempKind.Plan) is { } entry ? PlanJson.Deserialize(entry.Content) : null;

    private IActionResult Started(JobState? started)
    {
        if (started is not null) return Ok(ApiMapping.ToModel(started));

        // Another job is running (or claimed the slot first).
        return jobs.Current() is { } running ? Conflict(ApiMapping.ToModel(running)) : Conflict();
    }

    private StatusCodeResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden);

    private BadRequestObjectResult Error(string code) => BadRequest(new ErrorResponse { Error = code });
}
