using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;

namespace DynamicDave.Umbraco.MapToRedirect.Models;

/// <summary>Stable error codes of the API's 400, 404 and 409 responses (besides the CsvReadError names).</summary>
public static class ApiErrors
{
    public const string InvalidPaging = "invalidPaging";
    public const string InvalidFilter = "invalidFilter";
    public const string SiteNotFound = "siteNotFound";
    public const string CultureNotFound = "cultureNotFound";
    public const string SitemapUrlInvalid = "sitemapUrlInvalid";
    public const string MethodInvalid = "methodInvalid";
    public const string PlanExpired = "planExpired";
    public const string ImportNotFound = "importNotFound";
    public const string AlreadyUndone = "alreadyUndone";
    public const string ResultNotFound = "resultNotFound";
    public const string NoJobRunning = "noJobRunning";
}

public sealed class ErrorResponse
{
    public required string Error { get; init; }
}

public sealed class AccessResponse
{
    public required bool HasAccess { get; init; }
}

public sealed class SiteModel
{
    public required Guid RootKey { get; init; }
    public required string Name { get; init; }
    public required string[] Domains { get; init; }
    public required string[] Cultures { get; init; }
}

public sealed class GenerateRequest
{
    /// <summary>The old site's sitemap URL, or its domain.</summary>
    public required string SitemapUrl { get; init; }
    public required MatchMethod Method { get; init; }
    public required Guid RootKey { get; init; }
    public required string Culture { get; init; }
}

public sealed class ApplyRequest
{
    /// <summary>True: rows that conflict with an existing redirect replace it.</summary>
    public bool Overwrite { get; init; }
}

public sealed class OutcomeCountModel
{
    public required PlanOutcome Outcome { get; init; }
    public required int Count { get; init; }
}

public sealed class PlanRowModel
{
    public required int RowNumber { get; init; }
    public required string OldUrl { get; init; }
    public required string NewUrl { get; init; }
    public string? OldPath { get; init; }
    public string? Route { get; init; }
    public Guid? PageKey { get; init; }
    public string? PagePath { get; init; }
    public required PlanOutcome Outcome { get; init; }
    public required PlanReason Reason { get; init; }
    public Guid? CurrentPageKey { get; init; }
    public string? CurrentPagePath { get; init; }
    public required bool CurrentAllCultures { get; init; }
}

public sealed class PlanRowsResponse
{
    public required int Total { get; init; }
    public required PlanRowModel[] Items { get; init; }
}

public sealed class PlanResponse
{
    public required Guid PlanId { get; init; }
    public required string FileName { get; init; }
    /// <summary>One item per outcome, in the order of <see cref="PlanOutcome"/>, including outcomes without rows.</summary>
    public required OutcomeCountModel[] Counts { get; init; }
    public required PlanRowsResponse FirstPage { get; init; }
}

/// <summary>The state of the current or last job; <see cref="Summary"/> is JSON with counts for the UI.</summary>
public sealed class JobStateModel
{
    public required Guid Id { get; init; }
    public required JobKind Kind { get; init; }
    public required JobStatus Status { get; init; }
    public required Guid UserKey { get; init; }
    public required string UserName { get; init; }
    public required DateTime StartedUtc { get; init; }
    public required DateTime HeartbeatUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public int Done { get; init; }
    public int Total { get; init; }
    /// <summary>One of the JobPhases codes; empty before the first progress report.</summary>
    public required string Phase { get; init; }
    public Guid? ResultKey { get; init; }
    public TempKind? ResultKind { get; init; }
    public string? Error { get; init; }
    public string? Summary { get; init; }
    /// <summary>A cancel was requested; the job stops at its next progress write or heartbeat.</summary>
    public bool CancelRequested { get; init; }
}

public sealed class ImportCountsModel
{
    public int Added { get; init; }
    public int Replaced { get; init; }
    public int AlreadyPresent { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Changed { get; init; }
}

public sealed class ImportModel
{
    public required Guid Key { get; init; }
    public DateTime CreatedUtc { get; init; }
    public Guid UserKey { get; init; }
    public required string UserName { get; init; }
    public required string FileName { get; init; }
    public Guid RootKey { get; init; }
    public required string Culture { get; init; }
    public bool Overwrite { get; init; }
    public required ImportCountsModel Counts { get; init; }
    public required ImportState State { get; init; }
    public DateTime? UndoneUtc { get; init; }
    public string? UndoneBy { get; init; }
}

public sealed class ImportsResponse
{
    public required int Total { get; init; }
    public required ImportModel[] Items { get; init; }
}

public sealed class ImportRowModel
{
    public required int RowNumber { get; init; }
    public required string OldRoute { get; init; }
    public required string Culture { get; init; }
    public required ApplyOutcome Outcome { get; init; }
    public Guid? NewPageKey { get; init; }
    public Guid? ReplacedPageKey { get; init; }
    public DateTime? ReplacedCreateDateUtc { get; init; }
    public string? OldUrl { get; init; }
    public string? NewUrl { get; init; }
    public string? Message { get; init; }

    /// <summary>Stable code of <see cref="Message"/> (e.g. "PageExistsAtPath"); the dashboard localizes it.</summary>
    public string? MessageCode { get; init; }
    public bool ReplacedAllCultures { get; init; }
    public bool Undone { get; init; }
}

public sealed class ImportRowsResponse
{
    public required int Total { get; init; }
    public required ImportRowModel[] Items { get; init; }
}
