namespace DynamicDave.Umbraco.MapToRedirect.Planning;

/// <summary>The outcome of a CSV row in the test run (spec 4.4). Skip and Error rows are never executed.</summary>
public enum PlanOutcome
{
    Add,
    AlreadyPresent,
    Conflict,
    Skip,
    Error,
}

/// <summary>Why a row is skipped or in error; <see cref="None"/> for Add, AlreadyPresent and Conflict.</summary>
public enum PlanReason
{
    None,
    PageExistsAtPath,
    EmptyNewUrl,
    SameAsTarget,
    Duplicate,
    InvalidOldUrl,
    InvalidNewUrl,
    NotPublished,
    OtherSite,
}

/// <summary>One CSV row in the test run.</summary>
/// <param name="OldPath">The normalized old path; null when the old URL is invalid.</param>
/// <param name="Route">The redirect route of <paramref name="OldPath"/> (see Redirects.RedirectRoute).</param>
/// <param name="PageKey">The resolved target page, when the new URL resolved.</param>
/// <param name="PagePath">The path of the target page.</param>
/// <param name="CurrentPageKey">For a conflict: the page the route currently redirects to.</param>
/// <param name="CurrentPagePath">For a conflict: that page's path, when it is a published page of the site.</param>
public record PlanRow(
    int RowNumber,
    string OldUrl,
    string NewUrl,
    string? OldPath,
    string? Route,
    Guid? PageKey,
    string? PagePath,
    PlanOutcome Outcome,
    PlanReason Reason,
    Guid? CurrentPageKey,
    string? CurrentPagePath);

/// <summary>A test run of an uploaded CSV for one site and culture. <see cref="FileName"/> is at most 255 characters.</summary>
public record RedirectPlan(Guid Id, Guid RootKey, string Culture, string FileName, IReadOnlyList<PlanRow> Rows);
