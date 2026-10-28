using System.Text.Json.Serialization;
using DynamicDave.Umbraco.MapToRedirect.Persistence;

namespace DynamicDave.Umbraco.MapToRedirect.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<JobKind>))]
public enum JobKind
{
    Generate,
    Apply,
    Undo,
}

[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    Running,
    Completed,
    Failed,
    Aborted,
}

/// <summary>Stable phase codes of <see cref="JobState.Phase"/>; the dashboard localizes them.</summary>
public static class JobPhases
{
    public const string ReadingSitemap = "readingSitemap";
    public const string Matching = "matching";
    public const string FetchingTitles = "fetchingTitles";
    public const string WritingCsv = "writingCsv";
    public const string Applying = "applying";
    public const string Undoing = "undoing";
}

/// <summary>
/// The one background job of the installation, stored as JSON in umbracoKeyValue under <see cref="Constants.JobStateKey"/>.
/// <see cref="Phase"/> is one of <see cref="JobPhases"/> (empty before the first progress report); <see cref="Summary"/>
/// is JSON with counts for the UI; <see cref="ResultKey"/> and <see cref="ResultKind"/> point at a <see cref="TempStore"/> entry.
/// </summary>
public record JobState(
    Guid Id,
    JobKind Kind,
    JobStatus Status,
    Guid UserKey,
    string UserName,
    DateTime StartedUtc,
    DateTime HeartbeatUtc,
    DateTime? CompletedUtc,
    int Done,
    int Total,
    string Phase,
    Guid? ResultKey,
    TempKind? ResultKind,
    string? Error,
    string? Summary);

/// <summary>
/// Ends a job as <see cref="JobStatus.Failed"/> with a stable error code (e.g. a <c>SitemapError</c> name) and, optionally,
/// a summary for the UI.
/// </summary>
public sealed class JobFailedException(string error, string? summary = null) : Exception(error)
{
    public string Error { get; } = error;

    public string? Summary { get; } = summary;
}
