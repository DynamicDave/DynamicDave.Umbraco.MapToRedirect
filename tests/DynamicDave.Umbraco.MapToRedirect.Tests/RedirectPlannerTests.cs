using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Planning;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class RedirectPlannerTests
{
    private const string Culture = "nl-NL";

    private static readonly SitePage Home = new(Guid.NewGuid(), "/", "", "Home", null);
    private static readonly SitePage About = new(Guid.NewGuid(), "/over-ons", "over-ons", "Over ons", null);
    private static readonly SitePage Contact = new(Guid.NewGuid(), "/contact", "contact", "Contact", null);

    private static readonly IReadOnlyList<SitePage> Pages = [Home, About, Contact];

    private static readonly SiteInfo Site = new(
        Guid.NewGuid(), 1057, "Home", ["www.klant.nl", "klant.de"], [Culture, "de-DE"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Culture] = ["www.klant.nl"],
            ["de-DE"] = ["klant.de"],
        });

    private readonly FakeRedirectStore _store = new();

    private RedirectPlan Plan(params CsvRow[] rows) =>
        new RedirectPlanner(_store).Plan(Guid.NewGuid(), "redirects.csv", rows, Site, Culture, Pages);

    private PlanRow PlanOne(string oldUrl, string newUrl) => Assert.Single(Plan(new CsvRow(2, oldUrl, newUrl)).Rows);

    [Fact]
    public void New_redirect_is_add()
    {
        var row = PlanOne("https://oud.klant.nl/Oud/Wie-Zijn-Wij.php?x=1", "https://www.klant.nl/over-ons/");

        Assert.Equal(
            new PlanRow(2, "https://oud.klant.nl/Oud/Wie-Zijn-Wij.php?x=1", "https://www.klant.nl/over-ons/", "/oud/wie-zijn-wij",
                "1057/oud/wie-zijn-wij", About.Key, "/over-ons", PlanOutcome.Add, PlanReason.None, null, null),
            row);
    }

    [Fact]
    public void Same_redirect_is_already_present()
    {
        _store.Add("1057/oud/wie-zijn-wij", About.Key, Culture);

        var row = PlanOne("/oud/wie-zijn-wij", About.Key.ToString());

        Assert.Equal(PlanOutcome.AlreadyPresent, row.Outcome);
        Assert.Equal(PlanReason.None, row.Reason);
        Assert.Equal(About.Key, row.PageKey);
        Assert.Null(row.CurrentPageKey);
    }

    [Fact]
    public void Redirect_to_other_page_is_conflict()
    {
        _store.Add("1057/oud/wie-zijn-wij", Home.Key, Culture);
        _store.Add("1057/oud/wie-zijn-wij", Contact.Key, Culture);

        var row = PlanOne("/oud/wie-zijn-wij", "/over-ons");

        Assert.Equal(PlanOutcome.Conflict, row.Outcome);
        Assert.Equal(PlanReason.None, row.Reason);
        Assert.Equal(About.Key, row.PageKey);
        Assert.Equal("/over-ons", row.PagePath);
        Assert.Equal(Contact.Key, row.CurrentPageKey);
        Assert.Equal("/contact", row.CurrentPagePath);
    }

    [Fact]
    public void Conflict_with_a_page_outside_the_site_has_no_current_path()
    {
        var elsewhere = Guid.NewGuid();
        _store.Add("1057/oud", elsewhere, Culture);

        var row = PlanOne("/oud", "/over-ons");

        Assert.Equal(PlanOutcome.Conflict, row.Outcome);
        Assert.Equal(elsewhere, row.CurrentPageKey);
        Assert.Null(row.CurrentPagePath);
    }

    [Fact]
    public void Redirect_in_another_culture_does_not_count()
    {
        _store.Add("1057/oud", Contact.Key, "de-DE");

        Assert.Equal(PlanOutcome.Add, PlanOne("/oud", "/over-ons").Outcome);
    }

    [Fact]
    public void Old_url_that_is_a_published_page_is_skipped()
    {
        var row = PlanOne("https://oud.klant.nl/Contact/", "/over-ons");

        Assert.Equal(PlanOutcome.Skip, row.Outcome);
        Assert.Equal(PlanReason.PageExistsAtPath, row.Reason);
        Assert.Equal("/contact", row.OldPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_new_url_is_skipped(string newUrl)
    {
        var row = PlanOne("/oud", newUrl);

        Assert.Equal(PlanOutcome.Skip, row.Outcome);
        Assert.Equal(PlanReason.EmptyNewUrl, row.Reason);
        Assert.Null(row.PageKey);
    }

    [Fact]
    public void Same_old_and_new_path_is_skipped()
    {
        var row = PlanOne("https://oud.klant.nl/Over-Ons/", "https://www.klant.nl/over-ons");

        Assert.Equal(PlanOutcome.Skip, row.Outcome);
        Assert.Equal(PlanReason.SameAsTarget, row.Reason);
        Assert.Equal(About.Key, row.PageKey);
    }

    [Fact]
    public void Later_duplicate_old_url_is_skipped()
    {
        var plan = Plan(
            new CsvRow(2, "/oud/team.html", "/bestaat-niet"),
            new CsvRow(3, "https://oud.klant.nl/Oud/Team/", "/over-ons"),
            new CsvRow(4, "/oud/team", "/contact"),
            new CsvRow(5, "/oud/anders", "/contact"));

        Assert.Equal(
            [
                (2, PlanOutcome.Error, PlanReason.NotPublished),
                (3, PlanOutcome.Skip, PlanReason.Duplicate),
                (4, PlanOutcome.Skip, PlanReason.Duplicate),
                (5, PlanOutcome.Add, PlanReason.None),
            ],
            plan.Rows.Select(r => (r.RowNumber, r.Outcome, r.Reason)));
        Assert.Equal("/oud/team", plan.Rows[1].OldPath);
    }

    [Theory]
    [InlineData("/bestaat-niet")]
    [InlineData("https://www.klant.nl/bestaat-niet")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void Unknown_new_url_is_error(string newUrl)
    {
        var row = PlanOne("/oud", newUrl);

        Assert.Equal(PlanOutcome.Error, row.Outcome);
        Assert.Equal(PlanReason.NotPublished, row.Reason);
        Assert.Null(row.PageKey);
    }

    [Theory]
    [InlineData("https://www.anderesite.nl/over-ons")]
    [InlineData("https://klant.de/over-ons")]
    public void Full_url_on_other_host_is_error(string newUrl)
    {
        var row = PlanOne("/oud", newUrl);

        Assert.Equal(PlanOutcome.Error, row.Outcome);
        Assert.Equal(PlanReason.OtherSite, row.Reason);
        Assert.Null(row.PageKey);
    }

    [Theory]
    [InlineData("mailto:info@klant.nl")]
    [InlineData("ftp://klant.nl/over-ons")]
    public void Invalid_new_url_is_error(string newUrl)
    {
        var row = PlanOne("/oud", newUrl);

        Assert.Equal(PlanOutcome.Error, row.Outcome);
        Assert.Equal(PlanReason.InvalidNewUrl, row.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("mailto:info@klant.nl")]
    [InlineData("javascript:alert(1)")]
    public void Invalid_old_url_is_error(string oldUrl)
    {
        var row = PlanOne(oldUrl, "/over-ons");

        Assert.Equal(PlanOutcome.Error, row.Outcome);
        Assert.Equal(PlanReason.InvalidOldUrl, row.Reason);
        Assert.Null(row.OldPath);
        Assert.Null(row.Route);
    }

    [Fact]
    public void Plan_carries_id_site_culture_and_rows_in_file_order()
    {
        var id = Guid.NewGuid();
        var plan = new RedirectPlanner(_store).Plan(id, "redirects.csv", [new CsvRow(2, "/a", "/contact"), new CsvRow(3, "/b", "/contact")], Site, Culture, Pages);

        Assert.Equal(id, plan.Id);
        Assert.Equal(Site.RootKey, plan.RootKey);
        Assert.Equal(Culture, plan.Culture);
        Assert.Equal("redirects.csv", plan.FileName);
        Assert.Equal([2, 3], plan.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void File_name_is_cut_to_255_characters()
    {
        var plan = new RedirectPlanner(_store).Plan(Guid.NewGuid(), new string('a', 300) + ".csv", [], Site, Culture, Pages);

        Assert.Equal(new string('a', 255), plan.FileName);
    }

    [Fact]
    public void Planning_does_not_change_the_store()
    {
        Plan(new CsvRow(2, "/oud", "/contact"));

        Assert.Empty(_store.Entries);
    }
}
