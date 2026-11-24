using BookFinder.Domain;

namespace BookFinder.Application.Matching;

/// <summary>Deterministic ordering for classified candidates. The LLM never reorders across these rules.</summary>
public static class Ranker
{
    public const int MaxResults = 5;

    /// <summary>How far ahead of the runner-up (in reading-log entries) a same-strength top result must be to count as a clear winner.</summary>
    public const int ClearPopularityMultiple = 5;

    /// <summary>
    /// Tier → year named in the query → matched author's place on the work record → exact over near title →
    /// earlier (likelier) hypothesis → reading-log popularity → edition count → work key.
    /// </summary>
    public static readonly IComparer<RankedCandidate> Comparer = Comparer<RankedCandidate>.Create((x, y) =>
    {
        var c = CompareStrength(x, y);
        if (c != 0) return c;
        c = x.Evidence.HypothesisIndex.CompareTo(y.Evidence.HypothesisIndex);
        if (c != 0) return c;
        c = y.Book.ReadingLogCount.CompareTo(x.Book.ReadingLogCount);
        if (c != 0) return c;
        c = y.Book.EditionCount.CompareTo(x.Book.EditionCount);
        if (c != 0) return c;
        return string.CompareOrdinal(x.Book.WorkKey, y.Book.WorkKey);
    });

    public static IReadOnlyList<RankedCandidate> Rank(IEnumerable<RankedCandidate> candidates, int take = MaxResults) =>
        candidates.Order(Comparer).Take(take).ToList();

    /// <summary>
    /// A hero card is earned, not assumed: the top result must be a title+author match (A–C) and beat the
    /// runner-up on match strength, or be the same strength but far more widely read.
    /// </summary>
    public static bool HasClearWinner(IReadOnlyList<RankedCandidate> ranked)
    {
        if (ranked.Count == 0 || ranked[0].Tier > MatchTier.C) return false;
        if (ranked.Count == 1) return true;

        var (top, next) = (ranked[0], ranked[1]);
        if (CompareStrength(top, next) < 0) return true;
        return top.Book.ReadingLogCount >= ClearPopularityMultiple * Math.Max(1, next.Book.ReadingLogCount);
    }

    /// <summary>Match quality only (no popularity): negative when x is the stronger match.</summary>
    private static int CompareStrength(RankedCandidate x, RankedCandidate y)
    {
        var c = x.Tier.CompareTo(y.Tier);
        if (c != 0) return c;
        c = y.Evidence.YearMatched.CompareTo(x.Evidence.YearMatched);
        if (c != 0) return c;
        c = y.Evidence.AuthorRole.CompareTo(x.Evidence.AuthorRole);
        if (c != 0) return c;
        return y.Evidence.Title.CompareTo(x.Evidence.Title);
    }
}
