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
