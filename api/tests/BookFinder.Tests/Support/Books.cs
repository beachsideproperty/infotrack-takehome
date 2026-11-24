using BookFinder.Domain;

namespace BookFinder.Tests.Support;

/// <summary>Terse builders for hand-made candidates in unit tests.</summary>
public static class Books
{
    private static int _next;

    public static BookCandidate Book(
        string title,
        string[] authors,
        string[]? contributors = null,
        int? year = null,
        int readingLog = 0,
        int editions = 1,
        string? key = null) =>
        new(
            key ?? $"/works/OL{Interlocked.Increment(ref _next)}W",
            title,
            null,
            authors.Select(a => new AuthorRef(null, a)).ToList(),
            (contributors ?? []).Select(a => new AuthorRef(null, a)).ToList(),
            year,
            null,
            editions,
            readingLog);
}
