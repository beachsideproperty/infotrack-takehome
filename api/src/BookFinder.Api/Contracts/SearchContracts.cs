using BookFinder.Domain;

namespace BookFinder.Api.Contracts;

public sealed record SearchRequest(string? Query);

public sealed record SearchResponse(
    string Query,
    IReadOnlyList<InterpretedAs> InterpretedAs,
    string Mode,
    DidYouMean? DidYouMean,
    IReadOnlyList<BookResult> Results,
    bool HasBestMatch,
    string Parser,
    string Explanations);

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
    string Explanation);

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
            result.Explanation);
    }
}
