using DynamicDave.Umbraco.MapToRedirect.Planning;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

/// <summary>
/// Stable codes of the messages of import rows (<see cref="Persistence.ImportRowRecord.MessageCode"/>) and their English
/// text (<see cref="Persistence.ImportRowRecord.Message"/>, used in the report CSV). The dashboard localizes the codes.
/// Skip and error rows use the <see cref="PlanReason"/> name as code.
/// </summary>
public static class RowMessages
{
    public const string Skipped = "Skipped";
    public const string Error = "Error";
    public const string SiteRemoved = "SiteRemoved";
    public const string TargetUnpublished = "TargetUnpublished";
    public const string PageAtOldUrl = "PageAtOldUrl";
    public const string ConflictKept = "ConflictKept";
    public const string OverridesAllLanguages = "OverridesAllLanguages";
    public const string RedirectRemoved = "RedirectRemoved";
    public const string RedirectChanged = "RedirectChanged";

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        [nameof(PlanReason.PageExistsAtPath)] = "A published page exists at the old URL.",
        [nameof(PlanReason.EmptyNewUrl)] = "The new URL is empty.",
        [nameof(PlanReason.SameAsTarget)] = "The old URL is the URL of the target page.",
        [nameof(PlanReason.Duplicate)] = "The old URL appears earlier in the file.",
        [nameof(PlanReason.InvalidOldUrl)] = "The old URL is not valid.",
        [nameof(PlanReason.InvalidNewUrl)] = "The new URL is not valid.",
        [nameof(PlanReason.NotPublished)] = "The new URL is not a published page of the site.",
        [nameof(PlanReason.OtherSite)] = "The new URL belongs to another site or language.",
        [nameof(PlanReason.OutsideLanguagePath)] = "The old URL is outside the language path of the selected language.",
        [Skipped] = "Skipped.",
        [Error] = "Error.",
        [SiteRemoved] = "The site no longer exists.",
        [TargetUnpublished] = "The target page is no longer published.",
        [PageAtOldUrl] = "A published page now exists at the old URL.",
        [ConflictKept] = "An existing redirect to another page was kept.",
        [OverridesAllLanguages] = "Overrides a redirect for all languages, which was kept.",
        [RedirectRemoved] = "The existing redirect was removed since the test run.",
        [RedirectChanged] = "The old URL redirects to another page since the test run.",
    };

    /// <summary>The English text of a code.</summary>
    public static string English(string code) => Texts.GetValueOrDefault(code, code);
}
