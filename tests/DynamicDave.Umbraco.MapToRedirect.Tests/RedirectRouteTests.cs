using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class RedirectRouteTests
{
    private const string Culture = "nl-NL";

    private static SiteInfo Site(int rootId, params string[] domains) => new(
        Guid.NewGuid(), rootId, "Home", domains, [Culture],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [Culture] = domains });

    // Every domain of both cultures has a path prefix: klant.nl/en and klant.com/english (en-US), klant.nl/nl (nl-NL).
    private static SiteInfo PrefixedSite(int rootId) => new(
        Guid.NewGuid(), rootId, "Home", ["klant.nl", "klant.com"], ["en-US", Culture],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["klant.nl", "klant.com"], [Culture] = ["klant.nl"] },
        new Dictionary<string, IReadOnlyList<CultureDomain>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = [new("klant.nl", "/en"), new("klant.com", "/english")],
            [Culture] = [new("klant.nl", "/nl")],
        });

    // en-US has a prefixed domain (klant.nl/en) and one without a prefix (old-en.com).
    private static SiteInfo MixedSite(int rootId) => new(
        Guid.NewGuid(), rootId, "Home", ["klant.nl", "old-en.com"], ["en-US"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["klant.nl", "old-en.com"] },
        new Dictionary<string, IReadOnlyList<CultureDomain>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = [new("klant.nl", "/en"), new("old-en.com", "")],
        });

    [Fact]
    public void Site_with_domains_prefixes_the_root_id() =>
        Assert.Equal("1057/over-ons", RedirectRoute.For(Site(1057, "www.klant.nl"), Culture, "/over-ons"));

    [Fact]
    public void Site_without_domains_uses_the_path() =>
        Assert.Equal("/over-ons", RedirectRoute.For(Site(1057), Culture, "/over-ons"));

    // Pinned against the TestSite (Umbraco 17.7.1): renaming invariant pages made Umbraco's own redirect tracking
    // write url '1059/a-renamed-x' for a root with domain probe-a.test (nl-NL) and '/b-renamed-x' for a root without
    // a domain (culture ''); a variant page under the domain root got '1059/v-oud' (nl-NL) and '1059/v-old' (en-US,
    // a culture without a domain on that root: Umbraco still writes the root-id form).
    [Theory]
    [InlineData(1059, "probe-a.test", "nl-NL", "/a-renamed-x", "1059/a-renamed-x")]
    [InlineData(1059, "probe-a.test", "en-US", "/v-old", "1059/v-old")]
    [InlineData(1057, null, "en-US", "/b-renamed-x", "/b-renamed-x")]
    public void Matches_what_umbraco_redirect_tracking_writes(int rootId, string? domain, string culture, string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(domain is null ? Site(rootId) : Site(rootId, domain), culture, path));

    [Fact]
    public void Root_path_with_domains_is_the_root_id_and_a_slash() =>
        Assert.Equal("1057/", RedirectRoute.For(Site(1057, "klant.nl"), Culture, "/"));

    // Pinned against the TestSite: for page /en/c-renamed-x on domain probe-c.test/en Umbraco wrote '1061/c-renamed-x',
    // and a request for probe-c.test/en/c-renamed-x redirected through it.
    [Theory]
    [InlineData("/en/c-renamed-x", "1061/c-renamed-x")]
    [InlineData("/en/over-ons/team", "1061/over-ons/team")]
    [InlineData("/en", "1061/")]
    [InlineData("/english/x", "1061/x")]
    public void Path_prefix_of_the_culture_domain_is_removed(string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(PrefixedSite(1061), "en-us", path));

    // When every domain of the culture has a prefix, a path outside all of them is served by no domain of the culture.
    [Theory]
    [InlineData("/over-ons")]
    [InlineData("/nl/over-ons")]
    [InlineData("/eng/x")]
    public void Path_outside_every_prefix_of_the_culture_has_no_route(string path) =>
        Assert.Null(RedirectRoute.For(PrefixedSite(1061), "en-US", path));

    // A domain without a prefix serves every path: the route keeps the full path.
    [Theory]
    [InlineData("/en/about", "1062/en/about")]
    [InlineData("/x", "1062/x")]
    public void Culture_with_a_domain_without_prefix_keeps_the_path(string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(MixedSite(1062), "en-US", path));

    [Theory]
    [InlineData("/en", "/en/about.html", "1062/about.html")]
    [InlineData("/en", "/en", "1062/")]
    [InlineData("", "/en/about.html", "1062/en/about.html")]
    public void Route_on_a_domain_removes_that_domains_prefix(string prefix, string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(MixedSite(1062), new CultureDomain("klant.nl", prefix), path));

    [Fact]
    public void Route_on_a_site_without_domains_is_the_path() =>
        Assert.Equal("/a.html", RedirectRoute.For(Site(1057), (CultureDomain?)null, "/a.html"));
}
