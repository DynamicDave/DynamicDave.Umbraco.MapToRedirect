using DynamicDave.Umbraco.MapToRedirect.Sites;

namespace DynamicDave.Umbraco.MapToRedirect.Matching;

public enum MatchKind
{
    None,
    Guess,
    Likely,
    Exact,
}

public enum MatchNote
{
    None,
    SamePath,
    SameSlug,
    SimilarSlug,
    SimilarName,
    SimilarTitle,
    TitleUnavailable,
}

/// <param name="Page">The matched page; null when <paramref name="Kind"/> is <see cref="MatchKind.None"/>.</param>
public record MatchResult(MatchKind Kind, SitePage? Page, MatchNote Note);
