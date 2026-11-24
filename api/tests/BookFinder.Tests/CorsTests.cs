using BookFinder.Api.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookFinder.Tests;

public class CorsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AllowedOrigin = "https://book-finder.vercel.app";

    private HttpClient CreateClient() =>
        factory.WithWebHostBuilder(b => b.UseSetting(CorsSetup.ConfigKey, $"{AllowedOrigin}/, http://localhost:5173"))
            .CreateClient();

    [Fact]
    public async Task Allowed_origin_gets_cors_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await CreateClient().SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(AllowedOrigin, Assert.Single(values));
    }

    [Fact]
    public async Task Unknown_origin_gets_no_cors_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await CreateClient().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData(" https://a.app/ , https://b.app,,https://A.app ", new[] { "https://a.app", "https://b.app" })]
    public void ParseOrigins_trims_dedupes_and_drops_trailing_slashes(string? raw, string[] expected) =>
        Assert.Equal(expected, CorsSetup.ParseOrigins(raw));
}
