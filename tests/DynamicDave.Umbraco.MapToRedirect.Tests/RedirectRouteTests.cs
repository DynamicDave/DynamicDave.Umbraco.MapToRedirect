using DynamicDave.Umbraco.MapToRedirect.Redirects;
using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class RedirectRouteTests
{
    private static SiteInfo Site(int rootId, params string[] domains) => new(
        Guid.NewGuid(), rootId, "Home", domains, ["nl-NL"],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["nl-NL"] = domains });

    [Fact]
    public void Site_with_domains_prefixes_the_root_id() =>
        Assert.Equal("1057/over-ons", RedirectRoute.For(Site(1057, "www.klant.nl"), "/over-ons"));

    [Fact]
    public void Site_without_domains_uses_the_path() =>
        Assert.Equal("/over-ons", RedirectRoute.For(Site(1057), "/over-ons"));

    // Pinned against the TestSite (Umbraco 17.7.1): renaming invariant pages made Umbraco's own redirect tracking
    // write url '1059/a-renamed-x' for a root with domain probe-a.test (nl-NL) and '/b-renamed-x' for a root without
    // a domain (culture ''); a variant page under the domain root got '1059/v-oud' (nl-NL) and '1059/v-old' (en-US,
    // a culture without a domain on that root: Umbraco still writes the root-id form).
    [Theory]
    [InlineData(1059, "probe-a.test", "/a-renamed-x", "1059/a-renamed-x")]
    [InlineData(1059, "probe-a.test", "/v-old", "1059/v-old")]
    [InlineData(1057, null, "/b-renamed-x", "/b-renamed-x")]
    public void Matches_what_umbraco_redirect_tracking_writes(int rootId, string? domain, string path, string expected) =>
        Assert.Equal(expected, RedirectRoute.For(domain is null ? Site(rootId) : Site(rootId, domain), path));

    [Fact]
    public void Root_path_with_domains_is_the_root_id_and_a_slash() =>
        Assert.Equal("1057/", RedirectRoute.For(Site(1057, "klant.nl"), "/"));

    // Binding decision: the old path is used as-is after normalization, also on a domain with a path prefix
    // (klant.nl/en). See the Task 8 report: Umbraco itself writes the path relative to the domain ('1061/c-renamed-x'
    // for probe-c.test/en/c-renamed-x).
    [Fact]
    public void Path_prefix_of_a_domain_is_not_stripped_from_the_old_path() =>
        Assert.Equal("1061/en/over-ons", RedirectRoute.For(Site(1061, "klant.nl"), "/en/over-ons"));
}
