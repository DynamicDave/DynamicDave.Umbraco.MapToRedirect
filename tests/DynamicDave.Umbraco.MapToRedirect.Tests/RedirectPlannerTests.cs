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
                "1057/oud/wie-zijn-wij", About.Key, "/over-ons", PlanOutcome.Add, PlanReason.None, null, null, false),
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
    public void Exact_culture_conflict_is_not_for_all_cultures()
    {
        _store.Add("1057/oud", Contact.Key, Culture);

        Assert.False(PlanOne("/oud", "/over-ons").CurrentAllCultures);
    }

    [Fact]
    public void All_languages_redirect_to_other_page_is_conflict()
    {
        _store.Add("1057/oud", Contact.Key, "");

        var row = PlanOne("/oud", "/over-ons");

        Assert.Equal(PlanOutcome.Conflict, row.Outcome);
        Assert.Equal(Contact.Key, row.CurrentPageKey);
        Assert.Equal("/contact", row.CurrentPagePath);
        Assert.True(row.CurrentAllCultures);
    }

    [Fact]
    public void All_languages_redirect_to_the_same_page_is_already_present()
    {
        _store.Add("1057/oud", About.Key, "");

        var row = PlanOne("/oud", "/over-ons");

        Assert.Equal(PlanOutcome.AlreadyPresent, row.Outcome);
        Assert.Null(row.CurrentPageKey);
        Assert.False(row.CurrentAllCultures);
    }

    [Fact]
    public void Culture_redirect_overrides_an_all_languages_redirect()
    {
        _store.Add("1057/oud", About.Key, Culture);
        _store.Add("1057/oud", Contact.Key, "");

        Assert.Equal(PlanOutcome.AlreadyPresent, PlanOne("/oud", "/over-ons").Outcome);
    }

    [Fact]
    public void Route_drops_the_path_prefix_of_the_culture_domain_but_published_check_uses_the_full_path()
    {
        var en = new SitePage(Guid.NewGuid(), "/en/about", "about", "About", null);
        IReadOnlyList<SitePage> pages = [new(Guid.NewGuid(), "/en", "en", "Home", null), en];
        var site = new SiteInfo(
            Guid.NewGuid(), 1061, "Home", ["klant.nl"], ["en-US"],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["klant.nl"] },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["/en"] });

        var rows = new RedirectPlanner(_store).Plan(
            Guid.NewGuid(), "redirects.csv", [new CsvRow(2, "/en/old-about", "/en/about"), new CsvRow(3, "/en/about", "/en")], site, "en-US", pages).Rows;

        Assert.Equal(("/en/old-about", "1061/old-about", PlanOutcome.Add), (rows[0].OldPath, rows[0].Route, rows[0].Outcome));
        Assert.Equal((PlanOutcome.Skip, PlanReason.PageExistsAtPath), (rows[1].Outcome, rows[1].Reason));
    }

    [Fact]
    public void Old_urls_with_the_same_route_are_duplicates()
    {
        IReadOnlyList<SitePage> pages = [new(Guid.NewGuid(), "/en/about", "about", "About", null), new(Guid.NewGuid(), "/en/contact", "contact", "Contact", null)];
        var site = new SiteInfo(
            Guid.NewGuid(), 1061, "Home", ["klant.nl"], ["en-US"],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["klant.nl"] },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["/en"] });

        var rows = new RedirectPlanner(_store).Plan(
            Guid.NewGuid(), "redirects.csv", [new CsvRow(2, "/en/old", "/en/about"), new CsvRow(3, "/old", "/en/contact")], site, "en-US", pages).Rows;

        Assert.Equal((PlanOutcome.Add, "1061/old"), (rows[0].Outcome, rows[0].Route));
        Assert.Equal((PlanOutcome.Skip, PlanReason.Duplicate, "1061/old"), (rows[1].Outcome, rows[1].Reason, rows[1].Route));
    }

    [Fact]
    public void Existing_redirects_are_loaded_once_per_plan()
    {
        _store.Add("1057/a", Contact.Key, Culture);
        _store.Add("1057/b", Home.Key, "");

        var rows = Plan(new CsvRow(2, "/a", "/contact"), new CsvRow(3, "/b", "/contact"), new CsvRow(4, "/c", "/contact")).Rows;

        Assert.Equal([PlanOutcome.AlreadyPresent, PlanOutcome.Conflict, PlanOutcome.Add], rows.Select(r => r.Outcome));
        Assert.Equal(1, _store.LoadAllCalls);
        Assert.Equal(0, _store.FindCalls);
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
