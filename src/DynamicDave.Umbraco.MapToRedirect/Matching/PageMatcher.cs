using DynamicDave.Umbraco.MapToRedirect.Sites;
using DynamicDave.Umbraco.MapToRedirect.Urls;

namespace DynamicDave.Umbraco.MapToRedirect.Matching;

/// <summary>Matches old URL paths to the pages of the current site.</summary>
public class PageMatcher
{
    private const double SlugThreshold = 0.8;
    private const double TitleThreshold = 0.9;

    private readonly Dictionary<string, SitePage> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SitePage>> _bySlug = new(StringComparer.Ordinal);
    private readonly List<Entry> _entries;

    public PageMatcher(IReadOnlyList<SitePage> pages)
    {
        _entries = new List<Entry>(pages.Count);
        foreach (var page in pages)
        {
            _byPath.TryAdd(page.Path, page);
            if (page.Slug.Length > 0)
            {
                if (!_bySlug.TryGetValue(page.Slug, out var list))
                {
                    _bySlug[page.Slug] = list = [];
                }

                list.Add(page);
            }

            _entries.Add(new Entry(
                page,
                TextSimilarity.Tokens(page.Slug),
                TextSimilarity.Tokens(page.Name),
                TextSimilarity.Tokens(page.Title),
                TextSimilarity.Tokens(ParentOf(page.Path))));
        }
    }

    /// <summary>Matches on the URL alone. <paramref name="oldPath"/> may be a path or URL; it is normalized first.</summary>
    public MatchResult Match(string oldPath)
    {
        var path = UrlNormalizer.Normalize(oldPath);
        if (path is null)
        {
            return new MatchResult(MatchKind.None, null, MatchNote.None);
        }

        if (_byPath.TryGetValue(path, out var exact))
        {
            return new MatchResult(MatchKind.Exact, exact, MatchNote.SamePath);
        }

        var slug = UrlNormalizer.Slug(path);
        var parent = TextSimilarity.Tokens(ParentOf(path));

        if (slug.Length > 0 && _bySlug.TryGetValue(slug, out var sameSlug))
        {
            if (sameSlug.Count == 1)
            {
                return new MatchResult(MatchKind.Likely, sameSlug[0], MatchNote.SameSlug);
            }

            var best = sameSlug
                .Select(p => (Page: p, Parent: ParentScore(p, parent)))
                .OrderByDescending(x => x.Parent)
                .ThenBy(x => x.Page.Path, StringComparer.Ordinal)
                .First();
            return new MatchResult(MatchKind.Guess, best.Page, MatchNote.SameSlug);
        }

        var slugTokens = TextSimilarity.Tokens(slug);
        if (slugTokens.Count == 0)
        {
            return new MatchResult(MatchKind.None, null, MatchNote.None);
        }

        Entry? winner = null;
        double winnerScore = 0, winnerParent = 0;
        var winnerByName = false;
        foreach (var entry in _entries)
        {
            var slugScore = TextSimilarity.Dice(slugTokens, entry.SlugTokens);
            var nameScore = TextSimilarity.Dice(slugTokens, entry.NameTokens);
            var score = Math.Max(slugScore, nameScore);
            if (score < SlugThreshold)
            {
                continue;
            }

            var parentScore = TextSimilarity.Dice(parent, entry.ParentTokens);
            if (winner is null || score > winnerScore
                || (score == winnerScore && (parentScore > winnerParent
                    || (parentScore == winnerParent && string.CompareOrdinal(entry.Page.Path, winner.Page.Path) < 0))))
            {
                winner = entry;
                winnerScore = score;
                winnerParent = parentScore;
                winnerByName = nameScore > slugScore;
            }
        }

        return winner is null
            ? new MatchResult(MatchKind.None, null, MatchNote.None)
            : new MatchResult(MatchKind.Guess, winner.Page, winnerByName ? MatchNote.SimilarName : MatchNote.SimilarSlug);
    }

    /// <summary>
    /// Matches on the URL, then lets the old page's title (when known) raise a result that is not Exact to Likely.
    /// A missing title keeps the URL result and marks it <see cref="MatchNote.TitleUnavailable"/>.
    /// </summary>
    public MatchResult MatchWithTitle(string oldPath, string? oldTitle)
    {
        var byUrl = Match(oldPath);
        if (byUrl.Kind == MatchKind.Exact)
        {
            return byUrl;
        }

        var titleTokens = TextSimilarity.Tokens(oldTitle);
        if (titleTokens.Count == 0)
        {
            return byUrl with { Note = MatchNote.TitleUnavailable };
        }

        if (byUrl.Kind == MatchKind.Likely)
        {
            return byUrl;
        }
        // A title is only trusted when exactly one page has the top score; shared titles ("Nieuws") stay with the URL result.
        Entry? winner = null;
        var winnerScore = 0.0;
        var tied = false;
        foreach (var entry in _entries)
        {
            var score = Math.Max(
                TextSimilarity.Dice(titleTokens, entry.NameTokens),
                TextSimilarity.Dice(titleTokens, entry.TitleTokens));
            if (score < TitleThreshold)
            {
                continue;
            }

            if (winner is null || score > winnerScore)
            {
                winner = entry;
                winnerScore = score;
                tied = false;
            }
            else if (score == winnerScore && entry.Page != winner.Page)
            {
                tied = true;
            }
        }

        return winner is null || tied ? byUrl : new MatchResult(MatchKind.Likely, winner.Page, MatchNote.SimilarTitle);
    }

    private static double ParentScore(SitePage page, HashSet<string> oldParent) =>
        TextSimilarity.Dice(oldParent, TextSimilarity.Tokens(ParentOf(page.Path)));

    private static string ParentOf(string path)
    {
        var trimmed = path.TrimEnd('/');
        var index = trimmed.LastIndexOf('/');
        return index > 0 ? trimmed[..index] : string.Empty;
    }

    private sealed record Entry(
        SitePage Page,
        HashSet<string> SlugTokens,
        HashSet<string> NameTokens,
        HashSet<string> TitleTokens,
        HashSet<string> ParentTokens);
}
