using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class UmbracoRedirectStoreTests
{
    private static readonly Guid PageA = Guid.NewGuid();
    private static readonly Guid PageB = Guid.NewGuid();

    private readonly FakeRedirectUrlService _service = new();

    private UmbracoRedirectStore Store => new(_service);

    // Pinned against the TestSite (Umbraco 17.7.1): Umbraco's own tracking wrote culture 'nl-NL' / 'en-US' (ISO code,
    // original casing) for a variant page; the package registers the selected culture the same way.
    [Fact]
    public void Add_registers_the_route_page_and_culture_unchanged()
    {
        Store.Add("1059/v-oud", PageA, "nl-NL");

        var row = Assert.Single(_service.Rows);
        Assert.Equal(("1059/v-oud", PageA, "nl-NL"), (row.Url, row.ContentKey, row.Culture));
    }

    [Fact]
    public void Find_returns_the_redirect_in_effect_for_the_route_and_culture()
    {
        _service.Register("1057/oud", PageA, "nl-NL");
        _service.Register("1057/oud", PageB, "nl-NL");

        var found = Store.Find("1057/oud", "nl-nl");

        var redirect = Assert.Single(found);
        var newest = _service.Rows.Single(r => r.ContentKey == PageB);
        Assert.Equal(new ExistingRedirect(newest.Key, PageB, newest.CreateDateUtc, false), redirect);
    }

    // Observed: Umbraco's own tracking writes culture '' for invariant pages, and GetMostRecentRedirectUrl falls back
    // to such a row when the culture has none. That row is in effect for every culture.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Find_returns_a_culture_less_redirect_for_all_cultures(string? culture)
    {
        _service.Register("1057/oud", PageA, culture);

        var redirect = Assert.Single(Store.Find("1057/oud", "nl-NL"));
        Assert.Equal(PageA, redirect.PageKey);
        Assert.True(redirect.AllCultures);
    }

    [Fact]
    public void Find_prefers_the_culture_over_a_newer_culture_less_redirect()
    {
        _service.Register("1057/oud", PageA, "nl-NL");
        _service.Register("1057/oud", PageB, "");

        var redirect = Assert.Single(Store.Find("1057/oud", "nl-NL"));
        Assert.Equal((PageA, false), (redirect.PageKey, redirect.AllCultures));
    }

    [Fact]
    public void Find_compares_the_route_case_insensitively_and_treats_a_blank_culture_as_culture_less()
    {
        _service.Register("1057/Oud", PageA, " ");

        var redirect = Assert.Single(Store.Find("1057/oud", "nl-NL"));
        Assert.Equal((PageA, true), (redirect.PageKey, redirect.AllCultures));
    }

    [Fact]
    public void LoadAll_pages_through_every_redirect()
    {
        for (var i = 0; i < 2500; i++) _service.Register($"1057/oud-{i}", PageA, i % 2 == 0 ? "nl-NL" : null);

        var all = Store.LoadAll();

        Assert.Equal(2500, all.Count);
        Assert.Equal(2500, all.Select(r => r.Key).Distinct().Count());
        Assert.Equal([(0L, 1000), (1L, 1000), (2L, 1000)], _service.PageRequests);
        var row = _service.Rows.Single(r => r.Url == "1057/oud-1");
        Assert.Contains(new StoredRedirect(row.Key, "1057/oud-1", "", PageA, row.CreateDateUtc), all);
    }

    [Fact]
    public void Find_ignores_other_cultures_and_routes()
    {
        _service.Register("1057/oud", PageA, "de-DE");
        _service.Register("1057/oud/x", PageA, "nl-NL");

        Assert.Empty(Store.Find("1057/oud", "nl-NL"));
    }

    [Fact]
    public void RemoveExact_deletes_every_row_of_the_route_and_culture_only()
    {
        _service.Register("1057/oud", PageA, "nl-NL");
        _service.Register("1057/oud", PageB, "nl-nl");
        _service.Register("1057/oud", PageA, string.Empty);
        _service.Register("1057/oud", PageB, null);
        _service.Register("1057/oud", PageA, "de-DE");
        _service.Register("1057/ander", PageA, "nl-NL");

        Store.RemoveExact("1057/oud", "nl-NL");

        Assert.Equal(2, _service.Deleted.Count);
        Assert.Equal(
            [("1057/oud", ""), ("1057/oud", "(null)"), ("1057/oud", "de-DE"), ("1057/ander", "nl-NL")],
            _service.Rows.Select(r => (r.Url, r.Culture ?? "(null)")));
    }

    [Fact]
    public void RemoveExact_keeps_a_culture_less_redirect()
    {
        _service.Register("1057/oud", PageA, string.Empty);

        Store.RemoveExact("1057/oud", "nl-NL");

        Assert.Empty(_service.Deleted);
        Assert.Single(_service.Rows);
    }
}
