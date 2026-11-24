using System.Net;
using BookFinder.Application.Ports;
using BookFinder.Infrastructure.OpenLibrary;

namespace BookFinder.Tests.Infrastructure;

public class OpenLibraryClientTests
{
    private const string SearchJson = """
        {"numFound":2,"docs":[
          {"key":"/works/OL27482W","title":"The Hobbit","author_name":["J.R.R. Tolkien"],"author_key":["OL26320A"],
           "first_publish_year":1937,"cover_i":14627509,"edition_count":550,"readinglog_count":3000},
          {"key":"/works/OL219602W","title":"The Hobbit","author_name":["Charles Dixon","J.R.R. Tolkien"],"author_key":["OL2453782A","OL26320A"]}
        ]}
        """;

    [Fact]
    public async Task Parses_search_docs_and_leaves_missing_fields_null()
    {
        var client = Catalogs.OpenLibrary(new StubHandler((_, _) => Task.FromResult(StubHandler.Json(SearchJson))));

        var docs = await client.SearchAsync(new CatalogQuery(Title: "The Hobbit", Author: "Tolkien"), default);

        Assert.Equal(2, docs.Count);
        var hobbit = docs[0];
        Assert.Equal(("/works/OL27482W", "The Hobbit", 1937, 14627509, 550, 3000),
            (hobbit.WorkKey, hobbit.Title, hobbit.FirstPublishYear, hobbit.CoverId, hobbit.EditionCount, hobbit.ReadingLogCount));
        Assert.Equal(["J.R.R. Tolkien"], hobbit.AuthorNames);
        Assert.Equal(["OL26320A"], hobbit.AuthorKeys);
        Assert.Equal(["OL2453782A", "OL26320A"], docs[1].AuthorKeys);
        Assert.Null(docs[1].FirstPublishYear);
        Assert.Equal(0, docs[1].ReadingLogCount);
    }

    [Fact]
    public async Task Reads_primary_authors_from_the_work_record_in_order()
    {
        const string work = """
            {"title":"The Hobbit","covers":[-1, 8406766],"authors":[
              {"author":{"key":"/authors/OL2453782A"},"type":{"key":"/type/author_role"}},
              {"author":{"key":"/authors/OL26320A"},"type":{"key":"/type/author_role"}}]}
            """;
        var client = Catalogs.OpenLibrary(new StubHandler((_, _) => Task.FromResult(StubHandler.Json(work))));

        var record = await client.GetWorkAsync("/works/OL219602W", default);

        Assert.Equal(["OL2453782A", "OL26320A"], record!.AuthorKeys);
        Assert.Equal(8406766, record.CoverId);
    }

    [Fact]
    public async Task Missing_work_is_null_not_an_error()
    {
        var client = Catalogs.OpenLibrary(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));

        Assert.Null(await client.GetWorkAsync("/works/OL0W", default));
    }

    [Fact]
    public async Task Caches_by_url()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(SearchJson)));
        var client = Catalogs.OpenLibrary(handler);
        var query = new CatalogQuery(Title: "The Hobbit");

        await client.SearchAsync(query, default);
        await client.SearchAsync(query, default);
        await client.SearchAsync(query with { Title = "Dune" }, default);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Retries_a_server_error_once()
    {
        var handler = new StubHandler((_, call) => Task.FromResult(call == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : StubHandler.Json(SearchJson)));

        var docs = await Catalogs.OpenLibrary(handler).SearchAsync(new CatalogQuery(Text: "hobbit"), default);

        Assert.Equal(2, docs.Count);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Persistent_failure_is_CatalogUnavailable()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var ex = await Assert.ThrowsAsync<CatalogUnavailableException>(() => Catalogs.OpenLibrary(handler).SearchAsync(new CatalogQuery(Text: "x"), default));

        Assert.False(ex.RateLimited);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Timeout_is_CatalogUnavailable()
    {
        var handler = new StubHandler(async (_, _) => { await Task.Delay(TimeSpan.FromSeconds(5)); return StubHandler.Json(SearchJson); });
        var client = Catalogs.OpenLibrary(handler, new OpenLibraryOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => client.SearchAsync(new CatalogQuery(Text: "x"), default));
    }

    [Fact]
    public async Task Rate_limited_is_flagged_and_not_retried()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        var ex = await Assert.ThrowsAsync<CatalogUnavailableException>(() => Catalogs.OpenLibrary(handler).SearchAsync(new CatalogQuery(Text: "x"), default));

        Assert.True(ex.RateLimited);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Search_url_escapes_values_and_asks_only_for_needed_fields()
    {
        var url = OpenLibraryClient.BuildSearchUrl(new CatalogQuery(Title: "Les Misérables", Author: "Hugo & co", Sort: "readinglog", Limit: 5));

        Assert.Equal($"search.json?title=Les%20Mis%C3%A9rables&author=Hugo%20%26%20co&sort=readinglog&fields={Uri.EscapeDataString(OpenLibraryClient.SearchFields)}&limit=5", url);
    }

    [Theory]
    [InlineData(null, 1, "BookFinder/1.0 (+https://github.com/beachsideproperty/infotrack-takehome)")]
    [InlineData("reader@example.com", 3, "BookFinder/1.0 (reader@example.com)")]
    public void Identified_clients_get_the_higher_rate_limit(string? email, int perSecond, string userAgent)
    {
        var options = new OpenLibraryOptions { ContactEmail = email };

        Assert.Equal(perSecond, options.RequestsPerSecond);
        Assert.Equal(userAgent, options.UserAgent);
    }
}
