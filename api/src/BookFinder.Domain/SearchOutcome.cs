namespace BookFinder.Domain;

public enum SearchMode
{
    Match,
    AuthorOnly,
    NoMatch,
}

public sealed record SearchResult(RankedCandidate Candidate, string Explanation);

/// <param name="Author">For author-only searches: the Open Library author whose works these are.</param>
public sealed record SearchOutcome(
    string Query,
    ParsedQuery Parsed,
    SearchMode Mode,
    QueryHypothesis? DidYouMean,
    IReadOnlyList<SearchResult> Results,
    bool HasBestMatch,
    bool ExplanationsFromLlm,
    AuthorRef? Author = null);
