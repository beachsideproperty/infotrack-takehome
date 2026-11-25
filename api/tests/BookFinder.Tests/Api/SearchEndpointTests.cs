using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookFinder.Api.Endpoints;
using BookFinder.Application.Ports;
using BookFinder.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BookFinder.Tests.Endpoints;

public class SearchEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly QueryHypothesis Hobbit = new("The Hobbit", "J.R.R. Tolkien", ["illustrated", "deluxe"], 1937);

    /// <summary>The real app with Gemini faked and Open Library replayed from recorded fixtures.</summary>
    private HttpClient Client(ILlmQueryParser? parser = null, IBookCatalog? catalog = null, IExplanationWriter? explainer = null) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddScoped(_ => parser ?? FakeLlmParser.Returning(Hobbit));
            services.AddScoped(_ => catalog ?? Catalogs.OpenLibrary(new FixtureHandler()));
            services.AddScoped(_ => explainer ?? new UnavailableExplainer());
        })).CreateClient();

    private static Task<HttpResponseMessage> Post(HttpClient client, string? query) =>
        client.PostAsJsonAsync("/api/search", new { query });

    [Fact]
    public async Task Returns_the_contract_shape()
    {
        var response = await Post(Client(explainer: new EchoExplainer()), "tolkien hobbit illustrated deluxe 1937");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("tolkien hobbit illustrated deluxe 1937", root.GetProperty("query").GetString());
        Assert.Equal("match", root.GetProperty("mode").GetString());
        Assert.Equal("llm", root.GetProperty("parser").GetString());
        Assert.Equal("llm", root.GetProperty("explanations").GetString());
        Assert.True(root.GetProperty("hasBestMatch").GetBoolean());
        Assert.False(root.TryGetProperty("didYouMean", out _)); // nulls are omitted

        var interpreted = root.GetProperty("interpretedAs")[0];
        Assert.Equal("The Hobbit", interpreted.GetProperty("title").GetString());
        Assert.Equal(1937, interpreted.GetProperty("year").GetInt32());

        var top = root.GetProperty("results")[0];
        Assert.Equal("The Hobbit", top.GetProperty("title").GetString());
        Assert.Equal("J.R.R. Tolkien", top.GetProperty("authors")[0].GetString());
        Assert.Equal(1937, top.GetProperty("firstPublishYear").GetInt32());
        Assert.Equal("A", top.GetProperty("matchTier").GetString());
        Assert.Equal("OL27482W", top.GetProperty("openLibraryId").GetString());
        Assert.Equal("https://openlibrary.org/works/OL27482W", top.GetProperty("url").GetString());
        Assert.StartsWith("https://covers.openlibrary.org/b/id/", top.GetProperty("coverUrl").GetString());
        Assert.Equal("LLM says The Hobbit.", top.GetProperty("explanation").GetString());
        Assert.Equal(JsonValueKind.Array, top.GetProperty("contributors").ValueKind);

        var match = top.GetProperty("match");
        Assert.Equal("exact", match.GetProperty("title").GetString());
        Assert.Equal("primary", match.GetProperty("author").GetString());
        Assert.True(match.GetProperty("yearMatched").GetBoolean());
        Assert.False(match.GetProperty("keywordOnly").GetBoolean());

        // Dixon's graphic novel: Tolkien credited after Dixon → contributor, flagged as a derivative version.
        var dixon = root.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("authors")[0].GetString() == "Charles Dixon");
        Assert.Equal("B", dixon.GetProperty("matchTier").GetString());
        Assert.Equal("contributor", dixon.GetProperty("match").GetProperty("author").GetString());
        Assert.True(dixon.GetProperty("match").GetProperty("derivative").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(null)]
    public async Task Empty_query_is_400_with_the_inline_message(string? query)
    {
        var response = await Post(Client(), query);

        await AssertValidationProblem(response, SearchEndpoints.EmptyQueryMessage);
    }

    [Fact]
    public async Task Over_300_characters_is_400()
    {
        var response = await Post(Client(), new string('a', 301));

        await AssertValidationProblem(response, SearchEndpoints.TooLongMessage);
    }

    [Fact]
    public async Task Exactly_300_characters_is_accepted()
    {
        var response = await Post(Client(parser: FakeLlmParser.Returning()), new string('a', 300));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Not_a_book_is_200_no_match()
    {
        var body = await (await Post(Client(parser: FakeLlmParser.Returning()), "pizza recipe")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("noMatch", body.GetProperty("mode").GetString());
        Assert.Equal(0, body.GetProperty("results").GetArrayLength());
        Assert.False(body.TryGetProperty("aiIssue", out _)); // Gemini answered: nothing to report
    }

    [Fact]
    public async Task Gemini_down_falls_back_to_rules_and_templates()
    {
        var body = await (await Post(Client(parser: FakeLlmParser.Unavailable), "tolkien hobbit illustrated deluxe 1937")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("fallback", body.GetProperty("parser").GetString());
        Assert.Equal("template", body.GetProperty("explanations").GetString());
        Assert.Equal("overloaded", body.GetProperty("aiIssue").GetString());
        Assert.Equal("The Hobbit", body.GetProperty("results")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Open_library_down_is_502_problem_details()
    {
        var response = await Post(Client(catalog: new DownCatalog()), "tolkien hobbit");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Can't reach Open Library right now", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Open_library_rate_limiting_is_503_with_retry_after()
    {
        var response = await Post(Client(catalog: new DownCatalog(rateLimited: true)), "tolkien hobbit");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task Author_only_includes_a_link_to_all_their_works()
    {
        var client = Client(parser: FakeLlmParser.Returning(H.Hyp(null, "Charles Dickens")));

        var body = await (await Post(client, "dickens")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("authorOnly", body.GetProperty("mode").GetString());
        var author = body.GetProperty("author");
        Assert.Equal("Charles Dickens", author.GetProperty("name").GetString());
        Assert.Equal("OL24638A", author.GetProperty("openLibraryId").GetString());
        Assert.Equal("https://openlibrary.org/authors/OL24638A", author.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Too_many_searches_from_one_client_is_429_with_retry_after()
    {
        var client = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("SEARCH_RATE_LIMIT_PER_MINUTE", "2");
            b.ConfigureTestServices(s => s.AddScoped<ILlmQueryParser>(_ => FakeLlmParser.Returning()));
        }).CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await Post(client, "pizza recipe")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "pizza recipe")).StatusCode);
        var third = await Post(client, "pizza recipe");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(int.Parse(third.Headers.GetValues("Retry-After").Single()) > 0);
        var problem = await third.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Lots of searches at once", problem.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode); // health is never limited
    }

    [Fact]
    public async Task Malformed_json_is_400()
    {
        var response = await Client().PostAsync("/api/search", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cors_preflight_allows_json_posts_from_the_configured_origin()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("ALLOWED_ORIGINS", "https://book-finder.vercel.app")).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/search");
        request.Headers.Add("Origin", "https://book-finder.vercel.app");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("https://book-finder.vercel.app", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    private static async Task AssertValidationProblem(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(message, problem.GetProperty("detail").GetString());
        Assert.Equal(message, problem.GetProperty("errors").GetProperty("query")[0].GetString());
    }
}
