using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class ExtensionRedirectRulesTests
{
    private static readonly SiteInfo KlantNl = Site(1057, "Klant NL", ("nl-NL", ["klant-nl.localhost"]));
    private static readonly SiteInfo KlantDe = Site(1058, "Klant DE", ("en-US", ["klant-de.localhost"]));
    private static readonly SiteInfo Bilingual = Site(1059, "Bilingual", ("en-US", ["bi-en.localhost"]), ("nl-NL", ["bi-nl.localhost"]));

    private static readonly SiteInfo Prefixed = new(
        Guid.NewGuid(), 1061, "Prefixed", ["klant.nl"], ["en-US", "nl-NL"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["klant.nl"], ["nl-NL"] = ["klant.nl"] },
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["/en"], ["nl-NL"] = ["/nl"] });

    private static readonly SiteInfo NoDomains = new(
        Guid.NewGuid(), 1070, "No domains", [], ["en-US"], new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    private static readonly IReadOnlyList<SiteInfo> Sites = [KlantNl, KlantDe, Bilingual, Prefixed];

    private static SiteInfo Site(int rootId, string name, params (string Culture, string[] Hosts)[] domains) => new(
        Guid.NewGuid(), rootId, name, domains.SelectMany(d => d.Hosts).ToArray(), domains.Select(d => d.Culture).ToArray(),
        domains.ToDictionary(d => d.Culture, d => (IReadOnlyList<string>)d.Hosts, StringComparer.OrdinalIgnoreCase));

    [Theory]
    [InlineData("/a.html", true)]
    [InlineData("/a/b.php", true)]
    [InlineData("/x.aspx", true)]
    [InlineData("/About/Ons-Team.HTML", true)]
    [InlineData("/a.b5", true)]
    [InlineData("/a", false)]
    [InlineData("/a/", false)]
    [InlineData("/", false)]
    [InlineData("/a.b/c", false)]
    [InlineData("/a.html/", false)]
    [InlineData("/.well-known/x", false)]
    [InlineData("/.env", false)]
    [InlineData("/a.toolongext", false)]
    [InlineData("/a.", false)]
    [InlineData("/a.ht-ml", false)]
    public void Has_extension(string path, bool expected) =>
        Assert.Equal(expected, ExtensionRedirectRules.HasExtension(path));

    [Theory]
    [InlineData("/umbraco/backoffice/x.js")]
    [InlineData("/Umbraco/x.css")]
    [InlineData("/umbraco")]
    [InlineData("/media/abc/photo.jpg")]
    [InlineData("/App_Plugins/x/y.js")]
    [InlineData("/umbraco-package/x.json")]
    [InlineData("/install/x.html")]
    [InlineData("/custom-reserved/x.html")]
    public void Reserved_paths_are_ignored(string path) =>
        Assert.True(ExtensionRedirectRules.IsReserved(path, ExtensionRedirectRules.ParseReserved("~/custom-reserved/,")));

    [Theory]
    [InlineData("/about/ons-team.html")]
    [InlineData("/umbracox/a.html")]
    [InlineData("/mediafiles/a.html")]
    public void Other_paths_are_not_reserved(string path) =>
        Assert.False(ExtensionRedirectRules.IsReserved(path, []));

    [Fact]
    public void Umbraco_reserved_settings_are_parsed() =>
        Assert.Equal(["/app_plugins", "/install", "/mini-profiler-resources", "/umbraco", "/.well-known"],
            ExtensionRedirectRules.ParseReserved("~/app_plugins/,~/install/,~/mini-profiler-resources/,~/umbraco/,", "~/.well-known,"));

    [Theory]
    [InlineData("GET", "/about/ons-team.html", true)]
    [InlineData("HEAD", "/about/ons-team.html", true)]
    [InlineData("head", "/about/ons-team.html", true)]
    [InlineData("POST", "/about/ons-team.html", false)]
    [InlineData("GET", "/about/ons-team", false)]
    [InlineData("GET", "/media/a.html", false)]
    public void Only_get_and_head_requests_for_unreserved_extension_paths_are_candidates(string method, string path, bool expected) =>
        Assert.Equal(expected, ExtensionRedirectRules.IsCandidate(method, path, []));

    [Theory]
    [InlineData("klant-nl.localhost:44416", 1057, "nl-NL")]
    [InlineData("klant-nl.localhost", 1057, "nl-NL")]
    [InlineData("KLANT-NL.localhost:44416", 1057, "nl-NL")]
    [InlineData("klant-de.localhost:44416", 1058, "en-US")]
    [InlineData("bi-en.localhost:44416", 1059, "en-US")]
    [InlineData("bi-nl.localhost:44416", 1059, "nl-NL")]
    public void Host_resolves_to_site_and_culture(string host, int rootId, string culture)
    {
        var match = ExtensionRedirectRules.MatchSite(Sites, host, "/about/ons-team.html");

        Assert.NotNull(match);
        Assert.Equal(rootId, match.Site.RootId);
        Assert.Equal(culture, match.Culture);
    }

    [Theory]
    [InlineData("unknown.localhost:44416")]
    [InlineData("localhost:44416")]
    [InlineData("127.0.0.1:44416")]
    public void Unknown_host_resolves_to_nothing(string host) =>
        Assert.Null(ExtensionRedirectRules.MatchSite(Sites, host, "/about/ons-team.html"));

    [Theory]
    [InlineData("/en/about.html", "en-US")]
    [InlineData("/nl/over.html", "nl-NL")]
    [InlineData("/EN/about.html", "en-US")]
    public void Prefixed_domain_resolves_to_the_culture_of_that_domain(string path, string culture)
    {
        var match = ExtensionRedirectRules.MatchSite(Sites, "klant.nl", path);

        Assert.NotNull(match);
        Assert.Same(Prefixed, match.Site);
        Assert.Equal(culture, match.Culture);
    }

    [Fact]
    public void Prefixed_host_without_a_matching_prefix_resolves_to_nothing() =>
        Assert.Null(ExtensionRedirectRules.MatchSite(Sites, "klant.nl", "/de/about.html"));

    // Umbraco serves a site without domains on any host it does not know, looking redirects up by the bare path.
    [Fact]
    public void Unknown_host_resolves_to_a_site_without_domains()
    {
        var match = ExtensionRedirectRules.MatchSite([KlantNl, NoDomains], "www.other.test", "/a.html");

        Assert.NotNull(match);
        Assert.Same(NoDomains, match.Site);
        Assert.Equal("en-US", match.Culture);
    }

    [Theory]
    [InlineData("klant-nl.localhost:44416", "/about/ons-team.html", "1057/about/ons-team.html", "nl-NL")]
    [InlineData("klant-nl.localhost:44416", "/About//Ons-Team.HTML", "1057/about/ons-team.html", "nl-NL")]
    [InlineData("klant-nl.localhost:44416", "/caf%C3%A9.html", "1057/café.html", "nl-NL")]
    [InlineData("bi-nl.localhost:44416", "/oud/pagina.php", "1059/oud/pagina.php", "nl-NL")]
    [InlineData("klant.nl", "/en/about.aspx", "1061/about.aspx", "en-US")]
    public void Route_matches_redirect_route_for_the_resolved_site(string host, string requestPath, string expectedRoute, string culture)
    {
        var lookup = ExtensionRedirectRules.Lookup(Sites, host, requestPath);

        Assert.NotNull(lookup);
        Assert.Equal(expectedRoute, lookup.Route);
        Assert.Equal(culture, lookup.Culture);
        var site = Sites.Single(s => s.RootId == int.Parse(expectedRoute.Split('/')[0]));
        Assert.Equal(RedirectRoute.For(site, culture, Urls.UrlNormalizer.RoutePath(requestPath)!), lookup.Route);
    }

    [Fact]
    public void Lookup_for_an_unknown_host_is_null() =>
        Assert.Null(ExtensionRedirectRules.Lookup(Sites, "unknown.localhost", "/a.html"));

    [Theory]
    [InlineData("https://klant.nl/over-ons/", "", "https://klant.nl/over-ons/")]
    [InlineData("https://klant.nl/over-ons/", "?a=1&b=2", "https://klant.nl/over-ons/?a=1&b=2")]
    [InlineData("https://klant.nl/over-ons/?x=1", "?a=1", "https://klant.nl/over-ons/?x=1&a=1")]
    public void Query_string_is_preserved(string target, string query, string expected) =>
        Assert.Equal(expected, ExtensionRedirectRules.WithQuery(target, query));

    [Theory]
    [InlineData("/over-ons/ons-team/?a=1", "/over-ons/ons-team/?a=1")]
    [InlineData("/über/?q=ü", "/%C3%BCber/?q=%C3%BC")]
    [InlineData("/a%C3%BC/?q=%20", "/a%C3%BC/?q=%20")]
    [InlineData("/a b/😀", "/a%20b/%F0%9F%98%80")]
    [InlineData("https://café.example:8443/über/", "https://xn--caf-dma.example:8443/%C3%BCber/")]
    [InlineData("https://klant.nl", "https://klant.nl")]
    [InlineData("https://klant.nl/x?y=1#z", "https://klant.nl/x?y=1#z")]
    public void Location_is_ascii(string url, string expected) =>
        Assert.Equal(expected, ExtensionRedirectRules.AsciiSafe(url));

    // Umbraco stores domains as entered (Unicode); browsers send the punycode host, and the other way round.
    [Theory]
    [InlineData("café.nl", "xn--caf-dma.nl:443")]
    [InlineData("café.nl", "CAFÉ.nl")]
    [InlineData("xn--caf-dma.nl", "café.nl")]
    public void Hosts_are_compared_as_idn(string domain, string host)
    {
        var site = Site(1080, "Café", ("nl-NL", [domain]));

        var match = ExtensionRedirectRules.MatchSite([site], host, "/a.html");

        Assert.NotNull(match);
        Assert.Same(site, match.Site);
    }
}
