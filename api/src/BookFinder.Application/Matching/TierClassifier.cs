using BookFinder.Application.Text;
using BookFinder.Domain;

namespace BookFinder.Application.Matching;

/// <summary>
/// Places a candidate in the brief's hierarchy (A–E) against each hypothesis and keeps the best reading.
/// Pure and deterministic: everything it knows comes from the candidate and the hypotheses.
/// </summary>
public static class TierClassifier
{
    /// <summary>Best classification across hypotheses, or null when nothing about the candidate matches.</summary>
    public static RankedCandidate? Classify(BookCandidate candidate, IReadOnlyList<QueryHypothesis> hypotheses)
    {
        RankedCandidate? best = null;
        for (var i = 0; i < hypotheses.Count; i++)
        {
            var ranked = Classify(candidate, hypotheses[i], i);
            if (ranked is not null && (best is null || Ranker.Comparer.Compare(ranked, best) < 0)) best = ranked;
        }
        return best;
    }

    public static RankedCandidate? Classify(BookCandidate candidate, QueryHypothesis hypothesis, int hypothesisIndex)
    {
        var (title, role, author) = hypothesis.IsFreeText
            ? MatchFreeText(candidate, hypothesis.FreeText!)
            : (hypothesis.IsSeries ? TitleMatcher.CompareSeries(hypothesis.Title, candidate.Title) : TitleOf(candidate, hypothesis.Title),
               RoleOf(candidate, hypothesis.Author, out var matched), matched);

        var tier = TierFor(title, role);
        if (tier is null) return null;

        // Box sets and omnibuses are never the work someone means; keep them, but at the bottom tier.
        var collection = CollectionDetector.IsCollection(candidate.Title);
        if (collection) tier = MatchTier.E;

        var yearMatched = hypothesis.Year is { } year && candidate.FirstPublishYear == year;
        var evidence = new MatchEvidence(title, role, author, yearMatched, hypothesisIndex,
            SeriesMember: hypothesis.IsSeries && title != TitleMatch.None, Collection: collection);
        return new RankedCandidate(candidate, tier.Value, evidence);
    }

    /// <summary>
    /// Open Library has no "adaptor" role: Charles Dixon's graphic-novel Hobbit lists Dixon, Deming and Tolkien
    /// as authors. When another work with the same title lists the matched author first, that one is theirs and
    /// this one is someone else's version, so the matched author counts as a contributor here (tier B, not A).
    /// </summary>
    public static IReadOnlyList<RankedCandidate> DemoteDerivatives(IReadOnlyList<RankedCandidate> candidates)
    {
        var ledByMatchedAuthor = candidates
            .Where(c => c.Evidence.AuthorRole == AuthorRole.Lead)
            .Select(c => (Title: TitleVariants.Base(c.Book.Title), Author: TextNormalizer.Normalize(c.Evidence.MatchedAuthor?.Name)))
            .ToHashSet();

        return candidates.Select(c =>
        {
            if (c.Evidence.AuthorRole != AuthorRole.Primary) return c;
            var key = (TitleVariants.Base(c.Book.Title), TextNormalizer.Normalize(c.Evidence.MatchedAuthor?.Name));
            if (!ledByMatchedAuthor.Contains(key)) return c;

            var tier = c.Tier switch
            {
                MatchTier.A => MatchTier.B,
                MatchTier.C => MatchTier.C,
                _ => c.Tier,
            };
            return c with { Tier = tier, Evidence = c.Evidence with { AuthorRole = AuthorRole.Contributor, Derivative = true } };
        }).ToList();
    }

    /// <summary>Tier D: one of an author's top works, kept only if they're a primary author on the work record.</summary>
    public static RankedCandidate? ClassifyAuthorWork(BookCandidate candidate, string author)
    {
        var role = RoleOf(candidate, author, out var matched);
        return role >= AuthorRole.Primary
            ? new RankedCandidate(candidate, MatchTier.D, new MatchEvidence(TitleMatch.None, role, matched, false, 0))
            : null;
    }

    public static MatchTier? TierFor(TitleMatch title, AuthorRole role) => (title, role) switch
    {
        (TitleMatch.Exact, >= AuthorRole.Primary) => MatchTier.A,
        (TitleMatch.Exact, AuthorRole.Contributor) => MatchTier.B,
        (TitleMatch.Near, not AuthorRole.None) => MatchTier.C,
        (not TitleMatch.None, _) or (_, not AuthorRole.None) => MatchTier.E,
        _ => null,
    };

    public static AuthorRole RoleOf(BookCandidate candidate, string? author, out AuthorRef? matched)
    {
        matched = null;
        if (string.IsNullOrWhiteSpace(author)) return AuthorRole.None;

        for (var i = 0; i < candidate.PrimaryAuthors.Count; i++)
        {
            if (AuthorMatcher.Matches(author, candidate.PrimaryAuthors[i].Name))
            {
                matched = candidate.PrimaryAuthors[i];
                return i == 0 ? AuthorRole.Lead : AuthorRole.Primary;
            }
        }

        matched = candidate.Contributors.FirstOrDefault(c => AuthorMatcher.Matches(author, c.Name));
        return matched is null ? AuthorRole.None : AuthorRole.Contributor;
    }

    private static TitleMatch TitleOf(BookCandidate candidate, string? title) =>
        TitleMatcher.Compare(title, candidate.Title, candidate.Subtitle);

    /// <summary>
    /// Fallback parsing: we don't know which words are the author. If a candidate author's surname is in
    /// the text, those words are the author and the rest is compared against the title. Titles count as near
    /// when every remaining word is in them ("huckleberry"). A first name alone ("mark") counts as the author
    /// only when the rest of the text matches that book's title, so a common first name can't match on its own.
    /// </summary>
    private static (TitleMatch, AuthorRole, AuthorRef?) MatchFreeText(BookCandidate candidate, string freeText)
    {
        var tokens = TextNormalizer.Tokens(freeText).ToList();
        var role = AuthorRole.None;
        AuthorRef? matched = null;

        var people = candidate.PrimaryAuthors.Select((a, i) => (a, i == 0 ? AuthorRole.Lead : AuthorRole.Primary))
            .Concat(candidate.Contributors.Select(a => (a, AuthorRole.Contributor)));
        foreach (var (person, personRole) in people)
        {
            var nameTokens = TextNormalizer.Tokens(person.Name);
            if (nameTokens.Length > 0 && tokens.Contains(nameTokens[^1]))
            {
                (role, matched) = (personRole, person);
                tokens.RemoveAll(nameTokens.Contains);
                break;
            }
        }

        if (matched is null && candidate.LeadAuthor is { } lead)
        {
            var givenNames = TextNormalizer.Tokens(lead.Name).SkipLast(1).Where(t => t.Length >= 3).ToHashSet();
            var rest = tokens.Where(t => !givenNames.Contains(t)).ToList();
            if (rest.Count > 0 && rest.Count < tokens.Count && FreeTextTitle(candidate, rest) != TitleMatch.None)
                (role, matched, tokens) = (AuthorRole.Lead, lead, rest);
        }

        return (tokens.Count > 0 ? FreeTextTitle(candidate, tokens) : TitleMatch.None, role, matched);
    }

    private static TitleMatch FreeTextTitle(BookCandidate candidate, List<string> words)
    {
        var text = string.Join(' ', words);
        var match = TitleOf(candidate, text);
        return match == TitleMatch.None && TitleMatcher.HasAllWords(text, candidate.Title, candidate.Subtitle) ? TitleMatch.Near : match;
    }
}
