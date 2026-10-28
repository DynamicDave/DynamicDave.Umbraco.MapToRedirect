using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class RedirectRouteTests
{
    private const string Culture = "nl-NL";

    private static SiteInfo Site(int rootId, params string[] domains) => new(
        Guid.NewGuid(), rootId, "Home", domains, [Culture],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [Culture] = domains });

    private static SiteInfo PrefixedSite(int rootId) => new(
        Guid.NewGuid(), rootId, "Home", ["klant.nl"], ["en-US", Culture],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["klant.nl"], [Culture] = ["klant.nl"] },
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = ["/en"], [Culture] = ["/nl"] });

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
    public void Path_prefix_of_the_culture_domain_is_removed(string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(PrefixedSite(1061), "en-us", path));

    [Theory]
    [InlineData("/english/x", "1061/english/x")]
    [InlineData("/over-ons", "1061/over-ons")]
    [InlineData("/nl/over-ons", "1061/nl/over-ons")]
    public void Path_outside_the_culture_prefix_is_used_unchanged(string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(PrefixedSite(1061), "en-US", path));
}
