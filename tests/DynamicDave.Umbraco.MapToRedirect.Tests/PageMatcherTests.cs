using DynamicDave.Umbraco.MapToRedirect.Matching;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class PageMatcherTests
{
    private static SitePage Page(string path, string? name = null, string? title = null) =>
        new(Guid.NewGuid(), path, UrlNormalizer.Slug(path), name ?? UrlNormalizer.Slug(path), title);

    [Fact]
    public void Same_path_is_exact()
    {
        var page = Page("/over-ons/team");
        var result = new PageMatcher([page, Page("/contact")]).Match("/over-ons/team");

        Assert.Equal(MatchKind.Exact, result.Kind);
        Assert.Equal(MatchNote.SamePath, result.Note);
        Assert.Same(page, result.Page);
    }

    [Fact]
    public void Unique_same_slug_is_likely()
    {
        var page = Page("/about/team");
        var result = new PageMatcher([page, Page("/contact")]).Match("/old/team");

        Assert.Equal(MatchKind.Likely, result.Kind);
        Assert.Equal(MatchNote.SameSlug, result.Note);
        Assert.Same(page, result.Page);
    }

    [Fact]
    public void Ambiguous_same_slug_is_not_likely()
    {
        var best = Page("/products/team");
        var result = new PageMatcher([Page("/about/team"), best]).Match("/products/old/team");

        Assert.Equal(MatchKind.Guess, result.Kind);
        Assert.Equal(MatchNote.SameSlug, result.Note);
        Assert.Same(best, result.Page);
    }

    [Fact]
    public void Similar_slug_is_a_guess()
    {
        Assert.Equal(0.8, TextSimilarity.Score("summer-sale-2024", "summer-sale"), 10);
        var page = Page("/shop/summer-sale");
        var result = new PageMatcher([page]).Match("/news/summer-sale-2024");

        Assert.Equal(MatchKind.Guess, result.Kind);
        Assert.Equal(MatchNote.SimilarSlug, result.Note);
        Assert.Same(page, result.Page);
    }

    [Fact]
    public void Slug_below_threshold_is_none()
    {
        Assert.True(TextSimilarity.Score("summer-sale-2024-extra", "summer-sale") < 0.8);
        var result = new PageMatcher([Page("/shop/summer-sale")]).Match("/news/summer-sale-2024-extra");

        Assert.Equal(MatchKind.None, result.Kind);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Similar_name_is_a_guess()
    {
        var page = Page("/diensten/web", name: "Summer Sale");
        var result = new PageMatcher([page]).Match("/news/summer-sale");

        Assert.Equal(MatchKind.Guess, result.Kind);
        Assert.Equal(MatchNote.SimilarName, result.Note);
        Assert.Same(page, result.Page);
    }

    [Fact]
    public void Tie_is_broken_by_parent_path()
    {
        var wanted = Page("/products/our-team", name: "Team");
        var result = new PageMatcher([Page("/about/our-team", name: "Team"), wanted]).Match("/products/team");

        Assert.Equal(MatchKind.Guess, result.Kind);
        Assert.Same(wanted, result.Page);
    }

    [Fact]
    public void No_match_is_none()
    {
        var result = new PageMatcher([Page("/contact")]).Match("/something/else");

        Assert.Equal(MatchKind.None, result.Kind);
        Assert.Null(result.Page);
        Assert.Equal(MatchNote.None, result.Note);
    }

    [Fact]
    public void Title_raises_guess_to_likely()
    {
        var page = Page("/diensten/web", name: "Ons team", title: "Ons team");
        var matcher = new PageMatcher([page, Page("/contact")]);

        Assert.Equal(MatchKind.None, matcher.Match("/old/medewerkers").Kind);
        var result = matcher.MatchWithTitle("/old/medewerkers", "Ons Team");

        Assert.Equal(MatchKind.Likely, result.Kind);
        Assert.Equal(MatchNote.SimilarTitle, result.Note);
        Assert.Same(page, result.Page);
    }

    [Fact]
    public void Title_never_overrides_exact()
    {
        var exact = Page("/over-ons");
        var other = Page("/team", name: "Ons team");
        var result = new PageMatcher([exact, other]).MatchWithTitle("/over-ons", "Ons team");

        Assert.Equal(MatchKind.Exact, result.Kind);
        Assert.Same(exact, result.Page);
    }

    [Fact]
    public void Missing_title_keeps_url_result()
    {
        var page = Page("/about/team");
        var result = new PageMatcher([page]).MatchWithTitle("/old/team", null);

        Assert.Equal(MatchKind.Likely, result.Kind);
        Assert.Same(page, result.Page);
        Assert.Equal(MatchNote.TitleUnavailable, result.Note);
    }

    [Fact]
    public void Missing_title_on_exact_has_no_unavailable_note()
    {
        var result = new PageMatcher([Page("/a")]).MatchWithTitle("/a", null);

        Assert.Equal(MatchNote.SamePath, result.Note);
    }

    [Fact]
    public void Score_handles_unicode_and_empty()
    {
        Assert.Equal(1.0, TextSimilarity.Score("Café-Menü", "café menü"), 10);
        Assert.Equal(0.0, TextSimilarity.Score("", "abc"));
    }
}
