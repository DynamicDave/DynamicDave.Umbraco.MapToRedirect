using DynamicDave.Umbraco.MapToRedirect.Planning;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class PlanJsonTests
{
    [Fact]
    public void Round_trip_preserves_every_field()
    {
        var plan = new RedirectPlan(Guid.NewGuid(), Guid.NewGuid(), "nl-NL", "redirects.csv",
        [
            new PlanRow(2, "https://old.nl/a", "/b", "/a", "1057/a", Guid.NewGuid(), "/b", PlanOutcome.Conflict, PlanReason.None, Guid.NewGuid(), "/c", true),
            new PlanRow(3, "::", "", null, null, null, null, PlanOutcome.Error, PlanReason.InvalidOldUrl, null, null, false),
        ]);

        var copy = PlanJson.Deserialize(PlanJson.Serialize(plan));

        Assert.NotNull(copy);
        Assert.Equal(plan with { Rows = [] }, copy with { Rows = [] });
        Assert.Equal(plan.Rows, copy.Rows);
    }

    [Fact]
    public void Enums_are_stored_as_names()
    {
        var plan = new RedirectPlan(Guid.NewGuid(), Guid.NewGuid(), "nl-NL", "r.csv",
            [new PlanRow(2, "/a", "/b", "/a", "1/a", null, null, PlanOutcome.Skip, PlanReason.Duplicate, null, null, false)]);

        var json = PlanJson.Serialize(plan);

        Assert.Contains("\"Skip\"", json);
        Assert.Contains("\"Duplicate\"", json);
    }

    [Fact]
    public void Invalid_json_gives_null() => Assert.Null(PlanJson.Deserialize("{not json"));
}
