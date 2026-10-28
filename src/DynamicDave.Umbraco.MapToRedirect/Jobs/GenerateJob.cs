using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Matching;
using DynamicDave.Umbraco.MapToRedirect.Persistence;
using DynamicDave.Umbraco.MapToRedirect.Sitemaps;
using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<MatchMethod>))]
public enum MatchMethod
{
    Url,
    UrlAndTitle,
}

/// <summary>
/// Generates a redirect CSV (spec §4.3): reads the old site's sitemap, matches every old URL to a page of the selected
/// site and culture (optionally using the old page titles) and stores the CSV in <see cref="TempStore"/>.
/// </summary>
public sealed class GenerateJob
{
    public const string SiteNotFound = "SiteNotFound";

    private static readonly string[] Header = ["Old URL", "New URL", "Match", "Note"];
    private static readonly JsonSerializerOptions SummaryJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly SitemapReader _reader;
    private readonly PageTitleFetcher _titles;
    private readonly ISiteCatalog _catalog;
    private readonly Action<Guid, TempKind, string, string?> _put;
    private readonly TimeProvider _timeProvider;

    public GenerateJob(SitemapReader reader, PageTitleFetcher titles, ISiteCatalog catalog, TempStore store, TimeProvider timeProvider)
        : this(reader, titles, catalog, store.Put, timeProvider)
    {
    }

    internal GenerateJob(SitemapReader reader, PageTitleFetcher titles, ISiteCatalog catalog, Action<Guid, TempKind, string, string?> put, TimeProvider timeProvider)
    {
        _reader = reader;
        _titles = titles;
        _catalog = catalog;
        _put = put;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Returns the <see cref="TempKind.GeneratedCsv"/> key and the summary JSON. A sitemap that cannot be read (or an
    /// unknown site) throws <see cref="JobFailedException"/> with the error code, and no CSV is stored.
    /// </summary>
    public async Task<(Guid? ResultKey, TempKind? ResultKind, string? Summary)> RunAsync(
        string sitemapInput, MatchMethod method, Guid rootKey, string culture, JobProgress progress, CancellationToken ct)
    {
        var site = _catalog.GetSite(rootKey) ?? throw new JobFailedException(SiteNotFound);

        progress.Report(0, 0, JobPhases.ReadingSitemap);
        var read = await _reader.ReadAsync(sitemapInput, count => progress.Report(count, 0, JobPhases.ReadingSitemap), ct);
        var limits = read.LimitsReached.Select(l => l.ToString()).ToArray();
        if (read.Error is { } error)
        {
            throw new JobFailedException(error.ToString(), Summary(new Counts(), limits, error.ToString()));
        }

        var urls = read.Urls;
        var matcher = new PageMatcher(_catalog.GetPages(site, culture));
        var results = new MatchResult[urls.Count];
        for (var i = 0; i < urls.Count; i++)
        {
            progress.Report(i, urls.Count, JobPhases.Matching);
            results[i] = matcher.Match(urls[i]);
        }

        if (method == MatchMethod.UrlAndTitle)
        {
            var toFetch = Enumerable.Range(0, urls.Count).Where(i => results[i].Kind != MatchKind.Exact).ToList();
            progress.Report(0, toFetch.Count, JobPhases.FetchingTitles);
            var titles = await _titles.FetchAsync(
                toFetch.Select(i => urls[i]).ToList(),
                done => progress.Report(done, toFetch.Count, JobPhases.FetchingTitles),
                ct);
            foreach (var i in toFetch)
            {
                results[i] = matcher.MatchWithTitle(urls[i], titles[urls[i]]);
            }
        }

        progress.Report(0, urls.Count, JobPhases.WritingCsv);
        var counts = new Counts();
        var rows = Enumerable.Range(0, urls.Count)
            .OrderBy(i => results[i].Kind) // None → Guess → Likely → Exact; stable, so sitemap order within a kind.
            .Select(i =>
            {
                var result = results[i];
                counts.Add(result.Kind);
                return (IReadOnlyList<string>)
                [
                    UrlNormalizer.Normalize(urls[i]) ?? urls[i],
                    result.Kind == MatchKind.None ? string.Empty : result.Page?.Path ?? string.Empty,
                    result.Kind.ToString(),
                    NoteText(result.Note),
                ];
            })
            .ToList();

        // Stored as text: the BOM is kept as U+FEFF, so the download (UTF-8) starts with it again.
        var content = Encoding.UTF8.GetString(CsvFormat.Write(Header, rows));
        var key = Guid.NewGuid();
        _put(key, TempKind.GeneratedCsv, content, FileName(site.Name, culture, _timeProvider.GetUtcNow().UtcDateTime));
        progress.Report(urls.Count, urls.Count, JobPhases.WritingCsv);

        return (key, TempKind.GeneratedCsv, Summary(counts, limits, null));
    }

    /// <summary>The English note text of the CSV's Note column.</summary>
    internal static string NoteText(MatchNote note) => note switch
    {
        MatchNote.SamePath => "Page exists at this path; no redirect needed",
        MatchNote.SameSlug => "Same last URL segment",
        MatchNote.SimilarSlug => "Similar URL",
        MatchNote.SimilarName => "Similar to the page name",
        MatchNote.SimilarTitle => "Similar page title",
        MatchNote.TitleUnavailable => "Old page title could not be read",
        _ => string.Empty,
    };

    internal static string FileName(string siteName, string culture, DateTime utcNow) =>
        $"redirects-{Slug(siteName)}-{culture}-{utcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";

    /// <summary>
    /// Lower-case ASCII letters and digits (accents removed); every run of other characters becomes one '-', trimmed
    /// at both ends. "site" when nothing is left.
    /// </summary>
    internal static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        return slug.Length == 0 ? "site" : slug;
    }

    private static string Summary(Counts counts, string[] limits, string? error) => JsonSerializer.Serialize(
        new SummaryModel(counts.Urls, counts.Exact, counts.Likely, counts.Guess, counts.None, limits, error), SummaryJson);

    private sealed record SummaryModel(int Urls, int Exact, int Likely, int Guess, int None, string[] Limits, string? Error);

    private sealed class Counts
    {
        public int Urls { get; private set; }

        public int Exact { get; private set; }

        public int Likely { get; private set; }

        public int Guess { get; private set; }

        public int None { get; private set; }

        public void Add(MatchKind kind)
        {
            Urls++;
            switch (kind)
            {
                case MatchKind.Exact: Exact++; break;
                case MatchKind.Likely: Likely++; break;
                case MatchKind.Guess: Guess++; break;
                default: None++; break;
            }
        }
    }
}
