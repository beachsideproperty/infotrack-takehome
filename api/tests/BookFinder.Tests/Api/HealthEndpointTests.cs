using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookFinder.Tests.Endpoints;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed record Health(string Status, string Version);

    [Fact]
    public async Task Health_returns_ok_with_version()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Health>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }

    [Fact]
    public async Task Health_reports_gemini_status_without_secrets()
    {
        var json = await factory.CreateClient().GetStringAsync("/health");
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var status = doc.RootElement.GetProperty("ai").GetProperty("status").GetString();
        Assert.Contains(status, new[] { "ok", "limited", "unavailable", "notConfigured" });
        Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
