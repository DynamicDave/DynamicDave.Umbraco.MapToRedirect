using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class SitePageResolveTests
{
    private static readonly SitePage Home = new(Guid.NewGuid(), "/", "", "Home", null);
    private static readonly SitePage About = new(Guid.NewGuid(), "/over-ons", "over-ons", "Over ons", "Over ons | Klant");
    private static readonly SitePage Team = new(Guid.NewGuid(), "/over-ons/team", "team", "Team", null);

    private static readonly IReadOnlyList<SitePage> Pages = [Home, About, Team];

    private static readonly SiteInfo Site = new(
        Guid.NewGuid(), 1057, "Home", ["www.klant.nl", "klant.nl", "klant.de"], ["nl", "de"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["nl"] = ["www.klant.nl", "klant.nl"],
            ["de"] = ["klant.de"],
        });

    private static SitePage? Resolve(string newUrl, string culture = "nl") => SiteCatalogRules.Resolve(Site, culture, Pages, newUrl);

    [Fact]
    public void Resolves_a_document_key()
    {
        Assert.Same(Team, Resolve(Team.Key.ToString()));
        Assert.Same(About, Resolve(" " + About.Key.ToString("B").ToUpperInvariant() + " "));
    }

    [Fact]
    public void Unknown_document_key_does_not_resolve() =>
        Assert.Null(Resolve(Guid.NewGuid().ToString()));

    [Theory]
    [InlineData("/over-ons/team")]
    [InlineData("/Over-Ons/Team/")]
    [InlineData("over-ons/team")]
    [InlineData("/over-ons/team?x=1")]
    public void Resolves_a_path_with_trailing_slash_and_uppercase(string newUrl) =>
        Assert.Same(Team, Resolve(newUrl));

    [Fact]
    public void Resolves_the_root_path() =>
        Assert.Same(Home, Resolve("/"));

    [Theory]
    [InlineData("https://www.klant.nl/over-ons")]
    [InlineData("http://KLANT.nl/Over-Ons/")]
    public void Resolves_a_full_url_on_a_domain_of_the_selected_culture(string newUrl)
    {
        Assert.True(SiteCatalogRules.HostBelongsTo(Site, "nl", newUrl));
        Assert.Same(About, Resolve(newUrl));
    }

    [Fact]
    public void Culture_keys_are_compared_case_insensitively()
    {
        Assert.True(SiteCatalogRules.HostBelongsTo(Site, "DE", "https://klant.de/"));
        Assert.Same(Home, Resolve("https://klant.de/", "DE"));
    }

    [Theory]
    [InlineData("https://klant.de/")]
    [InlineData("https://klant.de/over-ons")]
    public void Full_url_on_a_domain_of_another_culture_of_the_same_site_does_not_resolve(string newUrl)
    {
        Assert.False(SiteCatalogRules.HostBelongsTo(Site, "nl", newUrl));
        Assert.Null(Resolve(newUrl));
    }

    [Fact]
    public void Full_url_on_another_host_does_not_resolve_and_does_not_belong_to_the_site()
    {
        const string newUrl = "https://www.andere-klant.nl/over-ons";

        Assert.Null(Resolve(newUrl));
        Assert.False(SiteCatalogRules.HostBelongsTo(Site, "nl", newUrl));
    }

    [Fact]
    public void Full_url_does_not_belong_to_a_site_without_domains()
    {
        var site = Site with { Domains = [], DomainsByCulture = new Dictionary<string, IReadOnlyList<string>>() };

        Assert.False(SiteCatalogRules.HostBelongsTo(site, "nl", "https://www.klant.nl/over-ons"));
        Assert.Null(SiteCatalogRules.Resolve(site, "nl", Pages, "https://www.klant.nl/over-ons"));
    }

    [Theory]
    [InlineData("/bestaat-niet")]
    [InlineData("/over-ons/team/extra")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mailto:info@klant.nl")]
    public void Unknown_or_invalid_value_does_not_resolve(string newUrl) =>
        Assert.Null(Resolve(newUrl));

    [Theory]
    [InlineData("www.Klant.nl", "www.klant.nl")]
    [InlineData("klant.nl/en", "klant.nl")]
    [InlineData("https://klant.nl:8443/en/", "klant.nl")]
    [InlineData("  ", null)]
    public void Domain_names_reduce_to_lower_case_hosts(string domainName, string? expected) =>
        Assert.Equal(expected, SiteCatalogRules.HostOfDomain(domainName));

    [Fact]
    public void Domains_are_grouped_by_culture()
    {
        var domains = SiteCatalogRules.BuildDomains(
            [new("www.Klant.nl", false, "nl"), new("klant.nl/en", false, "en"), new("klant.de", false, "de"), new("klant.nl", false, "NL")],
            () => throw new InvalidOperationException("default culture not needed"));

        Assert.Equal(["www.klant.nl", "klant.nl", "klant.de"], domains.Domains);
        Assert.Equal(["nl", "en", "de"], domains.Cultures);
        Assert.Equal(["www.klant.nl", "klant.nl"], domains.DomainsByCulture["nl"]);
        Assert.Equal(["klant.nl"], domains.DomainsByCulture["EN"]);
        Assert.Equal(["klant.de"], domains.DomainsByCulture["de"]);
    }

    [Fact]
    public void Domain_path_prefixes_are_grouped_by_culture()
    {
        var domains = SiteCatalogRules.BuildDomains(
            [
                new("klant.nl", false, "nl"), new("klant.nl/en", false, "en"), new("https://klant.com/EN/", false, "EN"),
                new("klant.nl/en/uk", false, "en-GB"), new("*1057", true, "de"),
            ],
            () => throw new InvalidOperationException("default culture not needed"));

        Assert.False(domains.PathPrefixesByCulture.ContainsKey("nl"));
        Assert.Equal(["/en"], domains.PathPrefixesByCulture["en"]);
        Assert.Equal(["/en/uk"], domains.PathPrefixesByCulture["EN-gb"]);
        Assert.False(domains.PathPrefixesByCulture.ContainsKey("de"));
    }

    [Fact]
    public void Wildcard_domain_counts_for_cultures_but_not_for_hosts()
    {
        var domains = SiteCatalogRules.BuildDomains([new("*1057", true, "nl-NL")], () => "en-US");

        Assert.Empty(domains.Domains);
        Assert.Empty(domains.DomainsByCulture);
        Assert.Equal(["nl-NL"], domains.Cultures);
    }

    [Fact]
    public void Root_without_domains_gets_the_default_culture()
    {
        var domains = SiteCatalogRules.BuildDomains([], () => "en-US");

        Assert.Empty(domains.Domains);
        Assert.Empty(domains.DomainsByCulture);
        Assert.Equal(["en-US"], domains.Cultures);
    }

    [Theory]
    [InlineData("/over-ons", true)]
    [InlineData("/Over-Ons/Team.html", true)]
    [InlineData("/", true)]
    [InlineData("/bestaat-niet", false)]
    public void IsPublishedPath_compares_normalized_paths(string path, bool expected) =>
        Assert.Equal(expected, SiteCatalogRules.IsPublishedPath(Pages, path));
}
