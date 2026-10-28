using System.Text.Json.Serialization;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

[JsonConverter(typeof(JsonStringEnumConverter<ApplyOutcome>))]
public enum ApplyOutcome
{
    Added,
    Replaced,
    AlreadyPresent,
    Skipped,
    Failed,
    Changed,
}
