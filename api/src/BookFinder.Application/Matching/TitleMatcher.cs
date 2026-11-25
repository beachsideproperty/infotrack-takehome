using BookFinder.Application.Text;
using BookFinder.Domain;

namespace BookFinder.Application.Matching;

public static class TitleMatcher
{
    public const double NearLevenshtein = 0.85;
    public const double NearDice = 0.8;

    /// <summary>Share of the candidate's meaningful words the query must cover for a containment match.</summary>
    public const double NearCoverage = 0.6;

    private static readonly HashSet<string> MinorWords = ["the", "a", "an", "and", "of", "or", "s", "in", "on", "to"];

    public static TitleMatch Compare(string? queryTitle, string title, string? subtitle = null)
    {
        var query = TextNormalizer.NormalizeTitle(queryTitle);
        if (query.Length == 0) return TitleMatch.None;

        var variants = TitleVariants.For(title, subtitle);
        if (variants.Contains(query)) return TitleMatch.Exact;

        foreach (var variant in variants)
        {
            if (IsNear(query, variant)) return TitleMatch.Near;
        }
        return TitleMatch.None;
    }

    private static bool IsNear(string query, string candidate)
    {
        if (StringSimilarity.LevenshteinRatio(query, candidate) >= NearLevenshtein) return true;

        var queryTokens = query.Split(' ');
        var candidateTokens = candidate.Split(' ');
        if (StringSimilarity.TokenDice(queryTokens, candidateTokens) >= NearDice) return true;

        // Containment: one title's meaningful words all appear in the other, and they cover most of it.
        // "two cities tale" ~ "a tale of two cities"; the series name "harry potter" doesn't cover
        // enough of "harry potter and the philosophers stone" to count.
        // Words match loosely ("hary" ~ "harry", "poter" ~ "potter").
        var a = Meaningful(queryTokens);
        var b = Meaningful(candidateTokens);
        if (a.Count == 0 || b.Count == 0) return false;
        var (smaller, larger) = a.Count <= b.Count ? (a, b) : (b, a);
        return smaller.All(s => larger.Any(l => WordsMatch(s, l))) && (double)smaller.Count / larger.Count >= NearCoverage;
    }

    /// <summary>
    /// Series query ("harry potter"): a work belongs if its title starts with the series name
    /// ("Harry Potter and the Chamber of Secrets"). Reported as a near match; a series has no exact answer.
    /// </summary>
    public static TitleMatch CompareSeries(string? seriesName, string title)
    {
        var series = TextNormalizer.NormalizeTitle(seriesName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = TextNormalizer.NormalizeTitle(title).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (series.Length == 0 || words.Length < series.Length) return TitleMatch.None;

        for (var i = 0; i < series.Length; i++)
        {
            if (!WordsMatch(series[i], words[i])) return TitleMatch.None;
        }
        return TitleMatch.Near;
    }

    /// <summary>
    /// Fallback parsing only: every meaningful word the reader typed is in the title, however much of the title is
    /// left over ("huckleberry" in "Adventures of Huckleberry Finn"). Too loose for a title Gemini read, but the
    /// fallback only has fragments, and the candidates already came from Open Library's relevance search.
    /// </summary>
    public static bool HasAllWords(string? text, string title, string? subtitle = null)
    {
        var words = Meaningful(TextNormalizer.NormalizeTitle(text).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (words.Count == 0) return false;
        var titleWords = TextNormalizer.NormalizeTitle($"{title} {subtitle}").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(w => titleWords.Any(t => WordsMatch(w, t)));
    }

    /// <summary>Equal, or a likely typo of a word of 4+ letters.</summary>
    public static bool WordsMatch(string a, string b) =>
        a == b || (Math.Min(a.Length, b.Length) >= 4 && StringSimilarity.LevenshteinRatio(a, b) >= 0.75);

    private static List<string> Meaningful(IEnumerable<string> tokens) => tokens.Where(t => !MinorWords.Contains(t)).Distinct().ToList();
}
