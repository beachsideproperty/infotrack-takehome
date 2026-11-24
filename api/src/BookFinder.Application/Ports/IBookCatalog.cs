namespace BookFinder.Application.Ports;

/// <summary>Read-only access to Open Library. Implementations handle rate limits and caching.</summary>
public interface IBookCatalog
{
    /// <summary><c>search.json</c>.</summary>
    Task<IReadOnlyList<CatalogDoc>> SearchAsync(CatalogQuery query, CancellationToken cancellationToken);

    /// <summary><c>works/{id}.json</c>; null when the work doesn't exist.</summary>
    Task<WorkRecord?> GetWorkAsync(string workKey, CancellationToken cancellationToken);

    /// <summary><c>authors/{id}.json</c> name; null when unknown.</summary>
    Task<string?> GetAuthorNameAsync(string authorKey, CancellationToken cancellationToken);

    /// <summary><c>search/authors.json</c>: candidate authors for a name, most prolific first.</summary>
    Task<IReadOnlyList<AuthorHit>> FindAuthorsAsync(string name, CancellationToken cancellationToken);
}

public sealed record CatalogQuery(
    string? Title = null,
    string? Author = null,
    string? Text = null,
    string? AuthorKey = null,
    string? Sort = null,
    int Limit = 10);

/// <summary>A search result row. Author names/keys are Open Library's merged list (primary authors and contributors).</summary>
public sealed record CatalogDoc(
    string WorkKey,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> AuthorNames,
    IReadOnlyList<string> AuthorKeys,
    int? FirstPublishYear,
    int? CoverId,
    int EditionCount,
    int ReadingLogCount);

/// <summary>The canonical work record: <see cref="AuthorKeys"/> are the primary authors, in listed order.</summary>
public sealed record WorkRecord(string WorkKey, string Title, string? Subtitle, IReadOnlyList<string> AuthorKeys, int? CoverId);

public sealed record AuthorHit(string Key, string Name, int WorkCount);

/// <summary>Open Library is down, slow or refusing us.</summary>
public sealed class CatalogUnavailableException(string message, bool rateLimited = false, Exception? inner = null)
    : Exception(message, inner)
{
    public bool RateLimited { get; } = rateLimited;
}
