namespace BookFinder.Domain;

/// <summary>Why a candidate matched: the facts the ranker and the explanations both use.</summary>
/// <param name="SeriesMember">The query named a series and this work's title starts with it.</param>
/// <param name="Collection">Box set, omnibus or multi-title volume rather than a single work.</param>
/// <param name="Derivative">Another work with this title lists the matched author first, so here they're credited
/// on someone else's version (an adaptation or graphic novel) and count as a contributor.</param>
public sealed record MatchEvidence(
    TitleMatch Title,
    AuthorRole AuthorRole,
    AuthorRef? MatchedAuthor,
    bool YearMatched,
    int HypothesisIndex,
    bool SeriesMember = false,
    bool Collection = false,
    bool Derivative = false);

public sealed record RankedCandidate(BookCandidate Book, MatchTier Tier, MatchEvidence Evidence);
