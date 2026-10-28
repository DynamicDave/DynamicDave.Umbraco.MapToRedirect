using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://oud.klant.nl/Over-Ons/Team.html?x=1#top", "/over-ons/team")]
    [InlineData("/over-ons/", "/over-ons")]
    [InlineData("/", "/")]
    [InlineData("https://klant.nl", "/")]
    [InlineData("/nieuws/index.html", "/nieuws")]
    [InlineData("/a/default.aspx", "/a")]
    [InlineData("/a/b.php", "/a/b")]
    [InlineData("//a///b//", "/a/b")]
    [InlineData("over-ons", "/over-ons")]
    [InlineData("/index.html", "/")]
    public void Normalize_returns_comparable_path(string input, string expected) =>
        Assert.Equal(expected, UrlNormalizer.Normalize(input));

    [Theory]
    [InlineData("mailto:x@y.z")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ftp://x/y")]
    public void Normalize_returns_null_for_non_paths(string? input) =>
        Assert.Null(UrlNormalizer.Normalize(input));

    [Fact]
    public void Variants_of_the_same_path_normalize_identically()
    {
        foreach (var variant in new[] { "/Caf%C3%A9 Menu/", "http://x.nl/café menu", "/CAFÉ%20MENU/index.html" })
        {
            Assert.Equal("/café menu", UrlNormalizer.Normalize(variant));
        }
    }

    [Fact]
    public void Slug_is_the_last_segment()
    {
        Assert.Equal("team", UrlNormalizer.Slug("/over-ons/team"));
        Assert.Equal("", UrlNormalizer.Slug("/"));
    }

    [Fact]
    public void HostOf_returns_lower_case_host_of_absolute_urls()
    {
        Assert.Equal("www.klant.nl", UrlNormalizer.HostOf("https://WWW.Klant.nl/x"));
        Assert.Null(UrlNormalizer.HostOf("/x"));
    }
}
