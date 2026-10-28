using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class SitePageResolveTests
{
    private static readonly SitePage Home = new(Guid.NewGuid(), "/", "", "Home", null);
    private static readonly SitePage About = new(Guid.NewGuid(), "/over-ons", "over-ons", "Over ons", "Over ons | Klant");
    private static readonly SitePage Team = new(Guid.NewGuid(), "/over-ons/team", "team", "Team", null);

    private static readonly IReadOnlyList<SitePage> Pages = [Home, About, Team];

    private static readonly SiteInfo Site = new(Guid.NewGuid(), 1057, "Home", ["www.klant.nl", "klant.nl"], ["nl"]);

    [Fact]
    public void Resolves_a_document_key()
    {
        Assert.Same(Team, SiteCatalogRules.Resolve(Site, Pages, Team.Key.ToString()));
        Assert.Same(About, SiteCatalogRules.Resolve(Site, Pages, " " + About.Key.ToString("B").ToUpperInvariant() + " "));
    }

    [Fact]
    public void Unknown_document_key_does_not_resolve() =>
        Assert.Null(SiteCatalogRules.Resolve(Site, Pages, Guid.NewGuid().ToString()));

    [Theory]
    [InlineData("/over-ons/team")]
    [InlineData("/Over-Ons/Team/")]
    [InlineData("over-ons/team")]
    [InlineData("/over-ons/team?x=1")]
    public void Resolves_a_path_with_trailing_slash_and_uppercase(string newUrl) =>
        Assert.Same(Team, SiteCatalogRules.Resolve(Site, Pages, newUrl));

    [Fact]
    public void Resolves_the_root_path() =>
        Assert.Same(Home, SiteCatalogRules.Resolve(Site, Pages, "/"));

    [Theory]
    [InlineData("https://www.klant.nl/over-ons")]
    [InlineData("http://KLANT.nl/Over-Ons/")]
    public void Resolves_a_full_url_on_a_site_domain(string newUrl)
    {
        Assert.True(SiteCatalogRules.HostBelongsTo(Site, newUrl));
        Assert.Same(About, SiteCatalogRules.Resolve(Site, Pages, newUrl));
    }

    [Fact]
    public void Full_url_on_another_host_does_not_resolve_and_does_not_belong_to_the_site()
    {
        const string newUrl = "https://www.andere-klant.nl/over-ons";

        Assert.Null(SiteCatalogRules.Resolve(Site, Pages, newUrl));
        Assert.False(SiteCatalogRules.HostBelongsTo(Site, newUrl));
    }

    [Fact]
    public void Full_url_does_not_belong_to_a_site_without_domains()
    {
        var site = Site with { Domains = [] };

        Assert.False(SiteCatalogRules.HostBelongsTo(site, "https://www.klant.nl/over-ons"));
        Assert.Null(SiteCatalogRules.Resolve(site, Pages, "https://www.klant.nl/over-ons"));
    }

    [Theory]
    [InlineData("/bestaat-niet")]
    [InlineData("/over-ons/team/extra")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mailto:info@klant.nl")]
    public void Unknown_or_invalid_value_does_not_resolve(string newUrl) =>
        Assert.Null(SiteCatalogRules.Resolve(Site, Pages, newUrl));

    [Theory]
    [InlineData("www.Klant.nl", "www.klant.nl")]
    [InlineData("klant.nl/en", "klant.nl")]
    [InlineData("https://klant.nl:8443/en/", "klant.nl")]
    [InlineData("  ", null)]
    public void Domain_names_reduce_to_lower_case_hosts(string domainName, string? expected) =>
        Assert.Equal(expected, SiteCatalogRules.HostOfDomain(domainName));

    [Theory]
    [InlineData("/over-ons", true)]
    [InlineData("/Over-Ons/Team.html", true)]
    [InlineData("/", true)]
    [InlineData("/bestaat-niet", false)]
    public void IsPublishedPath_compares_normalized_paths(string path, bool expected) =>
        Assert.Equal(expected, SiteCatalogRules.IsPublishedPath(Pages, path));
}
