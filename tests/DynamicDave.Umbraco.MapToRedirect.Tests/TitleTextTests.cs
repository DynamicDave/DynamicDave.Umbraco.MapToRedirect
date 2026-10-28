using DynamicDave.Umbraco.MapToRedirect.Sitemaps;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class TitleTextTests
{
    [Theory]
    [InlineData("<html><title>Ons team | Klant</title></html>", "Ons team")]
    [InlineData("<title>Ons team - Klant</title>", "Ons team")]
    [InlineData("<title>Ons team – Klant</title>", "Ons team")]
    [InlineData("<title>A | B | Klant</title>", "A | B")]
    [InlineData("<TITLE>\n  Over &amp; ons \n</TITLE>", "Over & ons")]
    [InlineData("<title>Zonder suffix</title>", "Zonder suffix")]
    public void Extracts_title(string html, string expected) =>
        Assert.Equal(expected, TitleText.Extract(html));

    [Fact]
    public void Falls_back_to_h1() =>
        Assert.Equal("Over & ons", TitleText.Extract("<body><h1 class=\"x\"> Over &amp;   ons </h1></body>"));

    [Fact]
    public void Empty_title_falls_back_to_h1() =>
        Assert.Equal("Kop", TitleText.Extract("<title> </title><h1>Kop</h1>"));

    [Fact]
    public void Neither_gives_null() =>
        Assert.Null(TitleText.Extract("<p>niets</p>"));
}

public class TitleTextHardeningTests
{
    [Fact]
    public void Ignores_comments() =>
        Assert.Equal("Echt", TitleText.Extract("<!-- <title>Oud</title> --><title>Echt</title>"));

    [Fact]
    public void Ignores_script_and_style_content() =>
        Assert.Equal("Echt", TitleText.Extract("<script>var s = '<title>Nep</title>';</script><style>/*<title>x</title>*/</style><title>Echt</title>"));

    [Fact]
    public void Svg_title_does_not_beat_h1() =>
        Assert.Equal("Kop", TitleText.Extract("<body><svg><title>Icoon</title></svg><h1>Kop</h1></body>"));

    [Theory]
    [InlineData("<title-x>Nee</title-x><h1>Kop</h1>")]
    [InlineData("<titlex>Nee</titlex><h1>Kop</h1>")]
    public void Tag_name_must_end_at_boundary(string html) =>
        Assert.Equal("Kop", TitleText.Extract(html));

    [Fact]
    public void Many_unclosed_tags_are_fast()
    {
        var html = string.Concat(Enumerable.Repeat("<title ", 200_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(TitleText.Extract(html));
        Assert.True(watch.ElapsedMilliseconds < 2000);
    }
}
