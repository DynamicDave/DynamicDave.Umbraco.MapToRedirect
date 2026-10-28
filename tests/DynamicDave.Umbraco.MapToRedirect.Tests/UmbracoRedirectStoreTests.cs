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
        Assert.Equal(new ExistingRedirect(newest.Key, PageB, newest.CreateDateUtc), redirect);
    }

    // Observed: GetMostRecentRedirectUrl falls back to a culture-less row (Umbraco's own tracking writes culture ''
    // for invariant pages); Find only reports rows of the exact culture.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Find_ignores_culture_less_rows(string? culture)
    {
        _service.Register("1057/oud", PageA, culture);

        Assert.Empty(Store.Find("1057/oud", "nl-NL"));
    }

    [Fact]
    public void Find_ignores_other_cultures_and_routes()
    {
        _service.Register("1057/oud", PageA, "de-DE");
        _service.Register("1057/oud/x", PageA, "nl-NL");

        Assert.Empty(Store.Find("1057/oud", "nl-NL"));
    }

    [Fact]
    public void RemoveAll_deletes_every_row_of_the_route_and_culture_only()
    {
        _service.Register("1057/oud", PageA, "nl-NL");
        _service.Register("1057/oud", PageB, "nl-nl");
        _service.Register("1057/oud", PageA, string.Empty);
        _service.Register("1057/oud", PageA, "de-DE");
        _service.Register("1057/ander", PageA, "nl-NL");

        Store.RemoveAll("1057/oud", "nl-NL");

        Assert.Equal(2, _service.Deleted.Count);
        Assert.Equal(
            [("1057/oud", ""), ("1057/oud", "de-DE"), ("1057/ander", "nl-NL")],
            _service.Rows.Select(r => (r.Url, r.Culture ?? "")));
    }
}
