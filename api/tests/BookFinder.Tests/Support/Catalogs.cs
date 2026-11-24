using BookFinder.Application.Ports;
using BookFinder.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookFinder.Tests.Support;

public static class Catalogs
{
    /// <summary>The real <see cref="OpenLibraryClient"/> over the given handler (no rate limiting; tests don't hit the network).</summary>
    public static OpenLibraryClient OpenLibrary(HttpMessageHandler handler, OpenLibraryOptions? options = null)
    {
        options ??= new OpenLibraryOptions();
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress, Timeout = options.Timeout };
        return new OpenLibraryClient(http, new MemoryCache(new MemoryCacheOptions()), Options.Create(options), NullLogger<OpenLibraryClient>.Instance);
    }
}

/// <summary>Catalog that always fails, for error-path tests.</summary>
public sealed class DownCatalog(bool rateLimited = false) : IBookCatalog
{
    private CatalogUnavailableException Fail() => new("test: Open Library down", rateLimited);

    public Task<IReadOnlyList<CatalogDoc>> SearchAsync(CatalogQuery query, CancellationToken cancellationToken) => throw Fail();

    public Task<WorkRecord?> GetWorkAsync(string workKey, CancellationToken cancellationToken) => throw Fail();

    public Task<string?> GetAuthorNameAsync(string authorKey, CancellationToken cancellationToken) => throw Fail();

    public Task<IReadOnlyList<AuthorHit>> FindAuthorsAsync(string name, CancellationToken cancellationToken) => throw Fail();
}
