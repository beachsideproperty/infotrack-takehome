using BookFinder.Domain;

namespace BookFinder.Api.Contracts;

public sealed record SearchRequest(string? Query);

public sealed record SearchResponse(
    string Query,
    IReadOnlyList<InterpretedAs> InterpretedAs,
    string Mode,
    DidYouMean? DidYouMean,
    AuthorLink? Author,
    IReadOnlyList<BookResult> Results,
    bool HasBestMatch,
    string Parser,
    string Explanations);

/// <summary>Author-only searches: who the works are by, with a link to all their works on Open Library.</summary>
public sealed record AuthorLink(string Name, string OpenLibraryId, string Url);

/// <summary>One reading of the query. <see cref="Text"/> is set only for the rule-based fallback.</summary>
public sealed record InterpretedAs(string? Title, string? Author, IReadOnlyList<string> Keywords, int? Year, bool IsSeries, string? Text);

public sealed record DidYouMean(string? Title, string? Author);

public sealed record BookResult(
    string Title,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Contributors,
    int? FirstPublishYear,
    string OpenLibraryId,
    string Url,
    string? CoverUrl,
    string MatchTier,
    MatchDetails Match,
    string Explanation);

/// <summary>What matched, so the UI can label a result precisely ("Exact title · primary author", "Keyword only").</summary>
/// <param name="Title">"exact" | "near" | "none".</param>
/// <param name="Author">"primary" | "contributor" | "none".</param>
public sealed record MatchDetails(
    string Title,
    string Author,
    bool YearMatched,
    bool SeriesMember,
    bool Collection,
    bool Derivative,
    bool KeywordOnly);

public static class SearchContractMapping
{
    public static SearchResponse ToResponse(this SearchOutcome outcome) => new(
        outcome.Query,
        outcome.Parsed.Hypotheses.Select(h => new InterpretedAs(h.Title, h.Author, h.Keywords, h.Year, h.IsSeries, h.FreeText)).ToList(),
        outcome.Mode switch
        {
            SearchMode.AuthorOnly => "authorOnly",
            SearchMode.NoMatch => "noMatch",
            _ => "match",
        },
        outcome.DidYouMean is { } d ? new DidYouMean(d.Title, d.Author) : null,
        outcome.Author is { Key: { Length: > 0 } key } a ? new AuthorLink(a.Name, key, $"https://openlibrary.org/authors/{key}") : null,
        outcome.Results.Select(ToResult).ToList(),
        outcome.HasBestMatch,
        outcome.Parsed.Parser == ParserKind.Llm ? "llm" : "fallback",
        outcome.ExplanationsFromLlm ? "llm" : "template");

    private static BookResult ToResult(SearchResult result)
    {
        var book = result.Candidate.Book;
        return new BookResult(
            book.Title,
            book.PrimaryAuthors.Select(a => a.Name).ToList(),
            book.Contributors.Select(a => a.Name).ToList(),
            book.FirstPublishYear,
            book.OpenLibraryId,
            $"https://openlibrary.org/works/{book.OpenLibraryId}",
            book.CoverId is { } id ? $"https://covers.openlibrary.org/b/id/{id}-L.jpg" : null,
            result.Candidate.Tier.ToString(),
            ToMatch(result.Candidate.Evidence),
            result.Explanation);
    }

    private static MatchDetails ToMatch(MatchEvidence e) => new(
        e.Title switch { TitleMatch.Exact => "exact", TitleMatch.Near => "near", _ => "none" },
        e.AuthorRole switch { AuthorRole.Lead or AuthorRole.Primary => "primary", AuthorRole.Contributor => "contributor", _ => "none" },
        e.YearMatched,
        e.SeriesMember,
        e.Collection,
        e.Derivative,
        e.KeywordOnly);
}
