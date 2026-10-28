namespace DynamicDave.Umbraco.MapToRedirect.Matching;

/// <summary>Token-based text similarity.</summary>
public static class TextSimilarity
{
    /// <summary>
    /// Dice coefficient (0..1) of the lower-case word sets of <paramref name="a"/> and <paramref name="b"/>; words are split
    /// at every character that is not a letter or digit ("summer-sale-2024" gives summer, sale, 2024).
    /// </summary>
    public static double Score(string a, string b) => Dice(Tokens(a), Tokens(b));

    internal static HashSet<string> Tokens(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var isWordChar = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        return tokens;
    }

    internal static double Dice(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var shared = a.Count <= b.Count ? a.Count(b.Contains) : b.Count(a.Contains);
        return 2.0 * shared / (a.Count + b.Count);
    }
}
