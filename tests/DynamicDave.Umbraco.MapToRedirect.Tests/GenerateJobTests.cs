using System.Net;
using System.Text.Json;
using DynamicDave.Umbraco.MapToRedirect.Jobs;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Sitemaps;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Tests.Fakes;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class GenerateJobTests
{
    private const string Culture = "nl-NL";
    private const string SitemapUrl = "https://old.example/sitemap.xml";

    private static readonly IPAddress[] Public = [IPAddress.Parse("93.184.216.34")];
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 5, 30, TimeSpan.Zero);

    private static readonly SiteInfo Site = new(
        Guid.NewGuid(), 1057, "Klant & Co.", ["www.klant.nl"], [Culture],
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [Culture] = ["www.klant.nl"] });

    private static readonly SitePage Contact = Page("/contact");
    private static readonly SitePage History = Page("/about/history");
    private static readonly SitePage Chairs = Page("/products/garden-chairs-teak");
    private static readonly SitePage Crew = Page("/about/our-crew", "Our team");

    private readonly FakeHttpHandler _http = new();
    private readonly List<(Guid Key, TempKind Kind, string Content, string? FileName)> _stored = [];
    private readonly List<string> _phases = [];

    private static SitePage Page(string path, string? name = null) =>
        new(Guid.NewGuid(), path, UrlNormalizer.Slug(path), name ?? UrlNormalizer.Slug(path), null);

    private static string UrlSet(params string[] paths) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + string.Concat(paths.Select(p => $"<url><loc>https://old.example{p}</loc></url>")) + "</urlset>";

    private GenerateJob Job(int maxUrls = 50_000) => new(
        new SitemapReader(_http, _ => Public, allowPrivate: false, maxUrls: maxUrls),
        new PageTitleFetcher(_http, _ => Public, allowPrivate: false),
        new FakeSiteCatalog(Site, [Contact, History, Chairs, Crew]),
        (key, kind, content, fileName) => _stored.Add((key, kind, content, fileName)),
        new FixedTimeProvider(Now));

    private JobProgress Progress() => new(new FixedTimeProvider(Now), (_, _, phase) =>
    {
        if (_phases.Count == 0 || _phases[^1] != phase) _phases.Add(phase);
    });

    private static string[][] Rows(string content)
    {
        Assert.StartsWith("﻿", content);
        return content[1..].Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(',')).ToArray();
    }

    [Fact]
    public async Task GenerateJob_writes_sorted_csv_with_match_columns()
    {
        _http.Text(SitemapUrl, UrlSet("/contact", "/old/history", "/nothing-here", "/garden-chairs-teak-wood"));

        var (key, kind, _) = await Job().RunAsync(SitemapUrl, MatchMethod.Url, Site.RootKey, Culture, Progress(), default);

        var stored = Assert.Single(_stored);
        Assert.Equal(key, stored.Key);
        Assert.Equal(TempKind.GeneratedCsv, kind);
        Assert.Equal(TempKind.GeneratedCsv, stored.Kind);
        Assert.Equal(
            [
                ["Old URL", "New URL", "Match", "Note"],
                ["/nothing-here", "", "None", ""],
                ["/garden-chairs-teak-wood", "/products/garden-chairs-teak", "Guess", "Similar URL"],
                ["/old/history", "/about/history", "Likely", "Same last URL segment"],
                ["/contact", "/contact", "Exact", "Page exists at this path; no redirect needed"],
            ],
            Rows(stored.Content));
    }

    [Fact]
    public async Task File_name_has_site_slug_culture_and_utc_time()
    {
        _http.Text(SitemapUrl, UrlSet("/contact"));

        await Job().RunAsync(SitemapUrl, MatchMethod.Url, Site.RootKey, Culture, Progress(), default);

        Assert.Equal("redirects-klant-co-nl-NL-20261028-1205.csv", Assert.Single(_stored).FileName);
    }

    [Fact]
    public async Task Summary_counts_matches_and_limits()
    {
        _http.Text(SitemapUrl, UrlSet("/contact", "/old/history", "/nothing-here", "/more"));

        var (_, _, summary) = await Job(maxUrls: 3).RunAsync(SitemapUrl, MatchMethod.Url, Site.RootKey, Culture, Progress(), default);

        Assert.Equal("""{"urls":3,"exact":1,"likely":1,"guess":0,"none":1,"limits":["Urls"],"error":null}""", summary);
    }

    [Fact]
    public async Task Title_method_fetches_only_non_exact_pages_and_notes_missing_titles()
    {
        _http.Text(SitemapUrl, UrlSet("/contact", "/xyz-123", "/missing"))
            .Text("https://old.example/xyz-123", "<title>Our team | Old site</title>", "text/html");

        await Job().RunAsync(SitemapUrl, MatchMethod.UrlAndTitle, Site.RootKey, Culture, Progress(), default);

        Assert.Equal(
            [
                ["Old URL", "New URL", "Match", "Note"],
                ["/missing", "", "None", "Old page title could not be read"],
                ["/xyz-123", "/about/our-crew", "Likely", "Similar page title"],
                ["/contact", "/contact", "Exact", "Page exists at this path; no redirect needed"],
            ],
            Rows(Assert.Single(_stored).Content));
        Assert.DoesNotContain("https://old.example/contact", _http.Requests);
        Assert.Equal([JobPhases.ReadingSitemap, JobPhases.Matching, JobPhases.FetchingTitles, JobPhases.WritingCsv], _phases);
    }

    [Fact]
    public async Task Url_method_reports_its_phases()
    {
        _http.Text(SitemapUrl, UrlSet("/contact"));

        await Job().RunAsync(SitemapUrl, MatchMethod.Url, Site.RootKey, Culture, Progress(), default);

        Assert.Equal([JobPhases.ReadingSitemap, JobPhases.Matching, JobPhases.WritingCsv], _phases);
    }

    [Fact]
    public async Task Sitemap_error_fails_the_job_without_a_csv()
    {
        var failure = await Assert.ThrowsAsync<JobFailedException>(
            () => Job().RunAsync(SitemapUrl, MatchMethod.Url, Site.RootKey, Culture, Progress(), default));

        Assert.Equal(nameof(SitemapError.Unreachable), failure.Error);
        Assert.Equal("Unreachable", JsonDocument.Parse(failure.Summary!).RootElement.GetProperty("error").GetString());
        Assert.Empty(_stored);
    }

    [Theory]
    [InlineData("Klant & Co.", "klant-co")]
    [InlineData("  Über Café 2026 ", "uber-cafe-2026")]
    [InlineData("***", "site")]
    public void Site_name_slug(string name, string expected) => Assert.Equal(expected, GenerateJob.Slug(name));
}
