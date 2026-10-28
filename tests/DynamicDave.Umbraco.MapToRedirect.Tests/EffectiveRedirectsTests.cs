using DynamicDave.Umbraco.MapToRedirect.Redirects;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class EffectiveRedirectsTests
{
    private static readonly Guid PageA = Guid.NewGuid();
    private static readonly Guid PageB = Guid.NewGuid();
    private static readonly DateTime Day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static StoredRedirect Row(string route, string culture, Guid page, int minute) =>
        new(Guid.NewGuid(), route, culture, page, Day.AddMinutes(minute));

    [Fact]
    public void Exact_culture_beats_a_newer_culture_less_redirect()
    {
        var exact = Row("1057/oud", "nl-NL", PageA, 1);
        var redirects = new EffectiveRedirects([exact, Row("1057/oud", "", PageB, 2)]);

        Assert.Equal(new ExistingRedirect(exact.Key, PageA, exact.CreateDateUtc, false), redirects.Find("1057/oud", "nl-nl"));
    }

    [Fact]
    public void Culture_less_redirect_serves_a_culture_without_one()
    {
        var all = Row("1057/oud", "", PageB, 1);
        var redirects = new EffectiveRedirects([all, Row("1057/oud", "de-DE", PageA, 2)]);

        Assert.Equal(new ExistingRedirect(all.Key, PageB, all.CreateDateUtc, true), redirects.Find("1057/oud", "nl-NL"));
    }

    [Fact]
    public void Newest_redirect_wins()
    {
        var newest = Row("1057/oud", "nl-NL", PageB, 3);
        var redirects = new EffectiveRedirects([Row("1057/oud", "nl-NL", PageA, 1), newest, Row("1057/oud", "nl-NL", PageA, 2)]);

        Assert.Equal(newest.Key, redirects.Find("1057/oud", "nl-NL")!.Key);
    }

    [Fact]
    public void Newest_culture_less_redirect_wins()
    {
        var newest = Row("1057/oud", " ", PageB, 3);
        var redirects = new EffectiveRedirects([Row("1057/oud", "", PageA, 1), newest]);

        Assert.Equal(newest.Key, redirects.Find("1057/oud", "nl-NL")!.Key);
    }

    [Fact]
    public void Route_is_compared_case_insensitively()
    {
        var redirects = new EffectiveRedirects([Row("1057/Oud", "nl-NL", PageA, 1)]);

        Assert.Equal(PageA, redirects.Find("1057/oud", "nl-NL")!.PageKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_culture_counts_as_culture_less(string culture)
    {
        var redirects = new EffectiveRedirects([Row("1057/oud", culture, PageA, 1)]);

        Assert.True(redirects.Find("1057/oud", "nl-NL")!.AllCultures);
    }

    [Fact]
    public void No_match_is_null()
    {
        var redirects = new EffectiveRedirects([Row("1057/oud", "de-DE", PageA, 1), Row("1057/oud/x", "nl-NL", PageA, 2)]);

        Assert.Null(redirects.Find("1057/oud", "nl-NL"));
        Assert.Null(new EffectiveRedirects([]).Find("1057/oud", "nl-NL"));
    }
}
