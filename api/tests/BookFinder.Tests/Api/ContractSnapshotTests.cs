using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BookFinder.Application.Ports;
using BookFinder.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BookFinder.Tests.Endpoints;

/// <summary>
/// Real API responses for each kind of result, saved as JSON in <c>contracts/</c> at the repo root. This test fails
/// when the API's output changes; <c>web/src/test/contract.test.ts</c> checks the same files against the web app's
/// TypeScript types. Together they stop the two sides drifting apart.
/// After an intended change: <c>UPDATE_CONTRACTS=1 dotnet test --filter Category=Contract</c>, then run the web tests.
/// </summary>
[Trait("Category", "Contract")]
public class ContractSnapshotTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private HttpClient Client(ILlmQueryParser parser, IExplanationWriter? explainer = null) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("GEMINI_API_KEY", "test-key"); // so /health doesn't depend on the machine's user-secrets
            b.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => parser);
                services.AddScoped<IBookCatalog>(_ => Catalogs.OpenLibrary(new FixtureHandler())); // never the live client
                services.AddScoped(_ => explainer ?? new UnavailableExplainer());
            });
        }).CreateClient();

    public static TheoryData<string, string> Searches => new()
    {
        { "search-best-match", "tolkien hobbit illustrated deluxe 1937" },
        { "search-author-only", "dickens" },
        { "search-series", "harry potter" },
        { "search-keyword-guesses", "blue cover book about a whale and a lighthouse" },
        { "search-not-a-book", "pizza recipe" },
        { "search-fallback", "tolkien hobbit illustrated deluxe 1937" },
    };

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Search_responses_match_the_saved_contract(string name, string query)
    {
        var client = name switch
        {
            "search-best-match" => Client(FakeLlmParser.Returning(new QueryHypothesis("The Hobbit", "J.R.R. Tolkien", ["illustrated", "deluxe"], 1937)), new EchoExplainer()),
            "search-author-only" => Client(FakeLlmParser.Returning(H.Hyp(null, "Charles Dickens"))),
            "search-series" => Client(FakeLlmParser.Returning(H.Hyp("Harry Potter", "J. K. Rowling", series: true))),
            "search-keyword-guesses" => Client(FakeLlmParser.Returning(new QueryHypothesis(null, null, ["whale", "lighthouse"], null))),
            "search-not-a-book" => Client(FakeLlmParser.Returning()),
            _ => Client(FakeLlmParser.Unavailable),
        };

        var response = await client.PostAsJsonAsync("/api/search", new { query });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode); // never snapshot an error by accident

        Check(name, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Validation_error_matches_the_saved_contract()
    {
        var response = await Client(FakeLlmParser.Returning()).PostAsJsonAsync("/api/search", new { query = "" });
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId"); // differs per request

        Check("problem-validation", body.ToJsonString());
    }

    [Fact]
    public async Task Health_matches_the_saved_contract()
    {
        var body = JsonNode.Parse(await Client(FakeLlmParser.Returning()).GetStringAsync("/health"))!.AsObject();
        body["version"] = "0.0.0"; // differs per build

        Check("health", body.ToJsonString());
    }

    private static void Check(string name, string json)
    {
        var actual = JsonSerializer.Serialize(JsonNode.Parse(json), Indented) + "\n";
        var path = Path.Combine(RepoRoot(), "contracts", name + ".json");

        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path), $"Missing {path}. Run UPDATE_CONTRACTS=1 dotnet test --filter Category=Contract.");
        Assert.True(File.ReadAllText(path) == actual,
            $"The API's {name} response changed. If intended: UPDATE_CONTRACTS=1 dotnet test --filter Category=Contract, update web/src/lib/types.ts, run npm test.\n{actual}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (docker-compose.yml) not found.");
    }
}
