using BookFinder.Application.Parsing;
using BookFinder.Application.Text;
using BookFinder.Domain;

namespace BookFinder.Application.Explanations;

/// <summary>
/// Deterministic "why this book" sentences, used when Gemini is unavailable. Every clause cites a field
/// we fetched: the title, the work record's authors, contributors, the year.
/// </summary>
public static class TemplateExplanations
{
    public static string For(RankedCandidate ranked, QueryHypothesis? hypothesis, int position)
    {
        var book = ranked.Book;
        var evidence = ranked.Evidence;
        var lead = book.LeadAuthor?.Name ?? "an unlisted author";
        var matched = evidence.MatchedAuthor?.Name ?? lead;

        if (evidence.KeywordOnly)
            return KeywordOnly(book, hypothesis);
        if (evidence.Collection)
            return $"Collection or multi-book volume by {lead}; listed below single works{(evidence.Title != TitleMatch.None ? " though the title overlaps" : string.Empty)}.";
        if (evidence.SeriesMember)
            return $"One of the books in the series you named; {lead} is the primary author on the work record. A series has no single match.";
        if (evidence.Derivative)
            return $"Exact title, but {lead} is listed first on this version; {matched} is credited after them, so it ranks below the original.";

        var sentence = ranked.Tier switch
        {
            MatchTier.A when evidence.AuthorRole == AuthorRole.Lead =>
                $"Exact title match; {lead} is the primary author on the work record{Contributors(book)}{Year(ranked)}.",
            MatchTier.A =>
                $"Exact title match; {matched} is credited on the work record, with {lead} listed first{Year(ranked)}.",
            MatchTier.B =>
                $"Exact title match, but {matched} is listed only as a contributor; {lead} is the primary author{Year(ranked)}.",
            MatchTier.C =>
                $"Close title match (\"{book.Title}\") and {matched} is {RolePhrase(evidence.AuthorRole)}{Year(ranked)}.",
            MatchTier.D =>
                $"Primary author on the work record. {(position == 0 ? "Most-read" : $"{Ordinal(position + 1)} most-read")} {matched} work in Open Library reading logs.",
            _ => Weak(ranked, hypothesis, lead, matched),
        };
        return sentence;
    }

    private static string Weak(RankedCandidate ranked, QueryHypothesis? hypothesis, string lead, string matched)
    {
        var evidence = ranked.Evidence;
        if (evidence.Title != TitleMatch.None)
        {
            var closeness = evidence.Title == TitleMatch.Exact ? "matches" : "is close to";
            return hypothesis?.HasAuthor == true
                ? $"The title {closeness} what you typed, but the author on record is {lead}, not {hypothesis.Author}."
                : $"The title {closeness} what you typed; {lead} is the primary author on the work record.";
        }
        return $"{matched} is {RolePhrase(evidence.AuthorRole)}, but \"{ranked.Book.Title}\" isn't the title you typed.";
    }

    private static string KeywordOnly(BookCandidate book, QueryHypothesis? hypothesis)
    {
        var titleWords = TextNormalizer.Tokens(book.Title).ToHashSet(StringComparer.Ordinal);
        var inTitle = QueryWords.Meaningful(hypothesis?.Keywords ?? []).Where(titleWords.Contains).ToList();
        return inTitle.Count switch
        {
            0 => "Open Library's keyword search matched this book, but no title or author words confirm it.",
            1 => $"\u201C{Capitalize(inTitle[0])}\u201D is in the title, but there was no title or author to confirm it.",
            _ => $"{string.Join(" and ", inTitle.Select(w => $"\u201C{Capitalize(w)}\u201D"))} are in the title, but there was no title or author to confirm it.",
        };
    }

    private static string Capitalize(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    private static string RolePhrase(AuthorRole role) => role switch
    {
        AuthorRole.Lead => "the primary author",
        AuthorRole.Primary => "a primary author",
        AuthorRole.Contributor => "listed as a contributor",
        _ => "not on the record",
    };

    private static string Contributors(BookCandidate book) => book.Contributors.Count switch
    {
        0 => string.Empty,
        1 => $"; {book.Contributors[0].Name} listed as a contributor",
        _ => $"; {book.Contributors[0].Name} and others listed as contributors",
    };

    private static string Year(RankedCandidate ranked) =>
        ranked.Evidence.YearMatched ? $"; first published in {ranked.Book.FirstPublishYear}, the year you gave" : string.Empty;

    private static string Ordinal(int n) =>
        $"{n}{(n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" })}";
}
