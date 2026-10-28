using System.Text.Json;
using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Models;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using Microsoft.AspNetCore.Mvc;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

// The package's controllers do not use Umbraco's backoffice JSON options ([JsonOptionsName]), so the API models
// serialize with MVC's default options. The generated TypeScript client expects enums as strings.
public class ApiJsonTests
{
    private static readonly JsonSerializerOptions Mvc = new JsonOptions().JsonSerializerOptions;

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Mvc);

    [Fact]
    public void Enums_serialize_as_strings()
    {
        var row = new PlanRowModel
        {
            RowNumber = 2, OldUrl = "/a", NewUrl = "/b", Outcome = PlanOutcome.AlreadyPresent, Reason = PlanReason.SameAsTarget, CurrentAllCultures = false,
        };
        Assert.Contains("\"outcome\":\"AlreadyPresent\"", Json(row));
        Assert.Contains("\"reason\":\"SameAsTarget\"", Json(row));
        Assert.Contains("\"outcome\":\"Conflict\"", Json(new OutcomeCountModel { Outcome = PlanOutcome.Conflict, Count = 1 }));

        Assert.Contains("\"method\":\"UrlAndTitle\"", Json(new GenerateRequest { SitemapUrl = "https://old.nl", Method = MatchMethod.UrlAndTitle, RootKey = Guid.NewGuid(), Culture = "nl-NL" }));

        var job = new JobStateModel
        {
            Id = Guid.NewGuid(), Kind = JobKind.Apply, Status = JobStatus.Completed, UserKey = Guid.NewGuid(), UserName = "Dave",
            StartedUtc = DateTime.UtcNow, HeartbeatUtc = DateTime.UtcNow, Phase = JobPhases.Applying, ResultKind = TempKind.Report,
        };
        Assert.Contains("\"status\":\"Completed\"", Json(job));
        Assert.Contains("\"kind\":\"Apply\"", Json(job));
        Assert.Contains("\"resultKind\":\"Report\"", Json(job));

        var importRow = new ImportRowModel { RowNumber = 2, OldRoute = "1057/a", Culture = "nl-NL", Outcome = ApplyOutcome.Replaced };
        Assert.Contains("\"outcome\":\"Replaced\"", Json(importRow));

        var import = new ImportModel
        {
            Key = Guid.NewGuid(), UserName = "Dave", FileName = "r.csv", Culture = "nl-NL", State = ImportState.Partial,
            Counts = new ImportCountsModel(),
        };
        Assert.Contains("\"state\":\"Partial\"", Json(import));
    }
}
