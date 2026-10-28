using System.Text;
using System.Text.Json;
using DynamicDave.Umbraco.MapToRedirect.Applying;
using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Models;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class ApiMappingTests
{
    private static readonly SiteInfo Site = new(
        Guid.NewGuid(), 1057, "Klant", ["klant.nl", "klant.de"], ["nl-NL", "de-DE"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["nl-NL"] = ["klant.nl"], ["de-DE"] = ["klant.de"] });

    private static PlanRow Row(int number, PlanOutcome outcome) =>
        new(number, $"/old-{number}", "/new", $"/old-{number}", $"1057/old-{number}", Guid.NewGuid(), "/new", outcome, PlanReason.None, null, null, false);

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 100, true)]
    [InlineData(50, 25, true)]
    [InlineData(-1, 25, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 101, false)]
    public void Paging_is_validated(int skip, int take, bool valid) => Assert.Equal(valid, Paging.IsValid(skip, take));

    [Fact]
    public void Site_and_culture_are_validated()
    {
        Assert.Equal((ApiErrors.SiteNotFound, null), ApiMapping.ValidateSite(null, "nl-NL"));
        Assert.Equal((ApiErrors.CultureNotFound, null), ApiMapping.ValidateSite(Site, "fr-FR"));
        Assert.Equal((ApiErrors.CultureNotFound, null), ApiMapping.ValidateSite(Site, ""));
        Assert.Equal((ApiErrors.CultureNotFound, null), ApiMapping.ValidateSite(Site, null));
        Assert.Equal((null, "de-DE"), ApiMapping.ValidateSite(Site, "DE-de"));
    }

    [Fact]
    public void Generate_request_is_validated()
    {
        GenerateRequest Request(string url, MatchMethod method = MatchMethod.Url) =>
            new() { SitemapUrl = url, Method = method, RootKey = Site.RootKey, Culture = "nl-NL" };

        Assert.Null(ApiMapping.ValidateGenerate(Request("https://old.nl/sitemap.xml")));
        Assert.Null(ApiMapping.ValidateGenerate(Request(new string('a', 2048))));
        Assert.Equal(ApiErrors.SitemapUrlInvalid, ApiMapping.ValidateGenerate(Request(" ")));
        Assert.Equal(ApiErrors.SitemapUrlInvalid, ApiMapping.ValidateGenerate(Request(null!)));
        Assert.Equal(ApiErrors.SitemapUrlInvalid, ApiMapping.ValidateGenerate(Request(new string('a', 2049))));
        Assert.Equal(ApiErrors.MethodInvalid, ApiMapping.ValidateGenerate(Request("https://old.nl", (MatchMethod)7)));
    }

    [Fact]
    public void Plan_response_has_counts_for_every_outcome_and_the_first_page()
    {
        var rows = Enumerable.Range(2, 30).Select(n => Row(n, n % 3 == 0 ? PlanOutcome.Conflict : PlanOutcome.Add)).ToList();
        var plan = new RedirectPlan(Guid.NewGuid(), Site.RootKey, "nl-NL", "r.csv", rows);

        var response = ApiMapping.ToPlanResponse(plan);

        Assert.Equal(plan.Id, response.PlanId);
        Assert.Equal("r.csv", response.FileName);
        Assert.Equal(Enum.GetValues<PlanOutcome>(), response.Counts.Select(c => c.Outcome));
        Assert.Equal(20, response.Counts.Single(c => c.Outcome == PlanOutcome.Add).Count);
        Assert.Equal(10, response.Counts.Single(c => c.Outcome == PlanOutcome.Conflict).Count);
        Assert.Equal(0, response.Counts.Single(c => c.Outcome == PlanOutcome.Error).Count);
        Assert.Equal(30, response.FirstPage.Total);
        Assert.Equal(25, response.FirstPage.Items.Length);
        Assert.Equal(2, response.FirstPage.Items[0].RowNumber);
    }

    [Fact]
    public void Plan_rows_are_filtered_and_paged()
    {
        var rows = Enumerable.Range(2, 10).Select(n => Row(n, n % 2 == 0 ? PlanOutcome.Add : PlanOutcome.Skip)).ToList();
        var plan = new RedirectPlan(Guid.NewGuid(), Site.RootKey, "nl-NL", "r.csv", rows);

        var page = ApiMapping.ToPlanRows(plan, [PlanOutcome.Skip], 1, 2);

        Assert.Equal(5, page.Total);
        Assert.Equal([5, 7], page.Items.Select(i => i.RowNumber));
        Assert.Equal(10, ApiMapping.ToPlanRows(plan, null, 0, 100).Total);
        Assert.Equal(10, ApiMapping.ToPlanRows(plan, [], 0, 100).Total);
    }

    [Fact]
    public void Plan_row_model_mirrors_the_row()
    {
        var row = new PlanRow(4, "https://old.nl/a", "/b", "/a", "1057/a", Guid.NewGuid(), "/b", PlanOutcome.Conflict, PlanReason.None, Guid.NewGuid(), "/c", true);

        var model = ApiMapping.ToModel(row);

        Assert.Equal(row, new PlanRow(model.RowNumber, model.OldUrl, model.NewUrl, model.OldPath, model.Route, model.PageKey, model.PagePath,
            model.Outcome, model.Reason, model.CurrentPageKey, model.CurrentPagePath, model.CurrentAllCultures));
    }

    [Fact]
    public void Import_models_mirror_the_records()
    {
        var record = new ImportRecord(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Dave", "r.csv", Guid.NewGuid(), "nl-NL", true,
            new ImportCounts(1, 2, 3, 4, 5, 6), ImportState.Undone, DateTime.UtcNow, "Eve");
        var model = ApiMapping.ToModel(record);
        Assert.Equal(record, new ImportRecord(model.Key, model.CreatedUtc, model.UserKey, model.UserName, model.FileName, model.RootKey, model.Culture,
            model.Overwrite, new ImportCounts(model.Counts.Added, model.Counts.Replaced, model.Counts.AlreadyPresent, model.Counts.Skipped,
                model.Counts.Failed, model.Counts.Changed), model.State, model.UndoneUtc, model.UndoneBy));

        var row = new ImportRowRecord(2, "1057/a", "nl-NL", ApplyOutcome.Replaced, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "/a", "/b", "m", true, true, "ConflictKept");
        var rowModel = ApiMapping.ToModel(row);
        Assert.Equal(row, new ImportRowRecord(rowModel.RowNumber, rowModel.OldRoute, rowModel.Culture, rowModel.Outcome, rowModel.NewPageKey,
            rowModel.ReplacedPageKey, rowModel.ReplacedCreateDateUtc, rowModel.OldUrl, rowModel.NewUrl, rowModel.Message, rowModel.ReplacedAllCultures,
            rowModel.Undone, rowModel.MessageCode));
    }

    [Fact]
    public void Job_state_model_mirrors_the_state()
    {
        var state = new JobState(Guid.NewGuid(), JobKind.Undo, JobStatus.Failed, Guid.NewGuid(), "Dave", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow,
            3, 9, JobPhases.Undoing, Guid.NewGuid(), TempKind.Report, "Unexpected", "{\"removed\":1}", CancelRequested: true);

        var model = ApiMapping.ToModel(state);

        Assert.Equal(state, new JobState(model.Id, model.Kind, model.Status, model.UserKey, model.UserName, model.StartedUtc, model.HeartbeatUtc,
            model.CompletedUtc, model.Done, model.Total, model.Phase, model.ResultKey, model.ResultKind, model.Error, model.Summary, model.CancelRequested));
    }

    [Fact]
    public void Site_model_mirrors_the_site()
    {
        var model = ApiMapping.ToModel(Site);

        Assert.Equal(Site.RootKey, model.RootKey);
        Assert.Equal("Klant", model.Name);
        Assert.Equal(["klant.nl", "klant.de"], model.Domains);
        Assert.Equal(["nl-NL", "de-DE"], model.Cultures);
    }

    [Fact]
    public void Report_csv_lists_every_row_with_its_outcome_and_message()
    {
        var rows = new[]
        {
            new ImportRowRecord(2, "1057/a", "nl-NL", ApplyOutcome.Added, Guid.NewGuid(), null, null, "https://old.nl/a", "/b", null),
            new ImportRowRecord(3, "", "nl-NL", ApplyOutcome.Failed, null, null, null, "::", "/c, d", "Invalid \"old\" URL."),
        };

        var csv = ApplyReport.Csv(rows);

        Assert.Equal("﻿Old URL,New URL,Outcome,Message\r\nhttps://old.nl/a,/b,Added,\r\n::,\"/c, d\",Failed,\"Invalid \"\"old\"\" URL.\"\r\n", csv);
    }

    [Fact]
    public void Summaries_are_camel_case_counts()
    {
        var apply = JsonDocument.Parse(ApplyReport.Summary(new ImportCounts(1, 2, 3, 4, 5, 6))).RootElement;
        Assert.Equal(1, apply.GetProperty("added").GetInt32());
        Assert.Equal(6, apply.GetProperty("changed").GetInt32());

        var undo = JsonDocument.Parse(ApplyReport.Summary(new UndoResult(7, 8, 1, []))).RootElement;
        Assert.Equal(7, undo.GetProperty("removed").GetInt32());
        Assert.Equal(8, undo.GetProperty("restored").GetInt32());
        Assert.Equal(1, undo.GetProperty("leftAlone").GetInt32());
        Assert.False(undo.TryGetProperty("leftAloneRows", out _));
    }

    [Fact]
    public void Download_bytes_have_exactly_one_bom()
    {
        var written = CsvFormat.Write(["Old URL"], [["/é"]]);
        var content = Encoding.UTF8.GetString(written);

        var bytes = ApiMapping.DownloadBytes(content);

        Assert.Equal(written, bytes);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal((byte)'O', bytes[3]);
    }

    [Fact]
    public void Upload_file_name_is_cut_to_255()
    {
        Assert.Equal(255, ApiMapping.UploadFileName(new string('a', 300) + ".csv").Length);
        Assert.Equal("r.csv", ApiMapping.UploadFileName("r.csv"));
    }

    [Fact]
    public void Report_file_name_has_the_time()
        => Assert.Equal("redirect-report-20261028-1405.csv", ApplyReport.FileName(new DateTime(2026, 10, 28, 14, 5, 0, DateTimeKind.Utc)));
}
