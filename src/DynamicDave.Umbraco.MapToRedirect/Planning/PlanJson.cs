using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicDave.Umbraco.MapToRedirect.Planning;

/// <summary>A <see cref="RedirectPlan"/> as stored in the TempStore between the test run and the apply.</summary>
public static class PlanJson
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static string Serialize(RedirectPlan plan) => JsonSerializer.Serialize(plan, Json);

    /// <summary>The plan, or null when the JSON is not a plan.</summary>
    public static RedirectPlan? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RedirectPlan>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
