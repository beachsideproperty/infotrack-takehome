using System.Net;
using System.Text.Json.Nodes;
using BookFinder.Application.Ports;
using BookFinder.Infrastructure.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookFinder.Tests.Infrastructure;

public class GeminiClientTests
{
    private static readonly JsonObject Schema = new() { ["type"] = "object" };

    private sealed record Answer(string Value);

    private static GeminiClient Client(StubHandler handler, string? apiKey = "test-key", TimeSpan? primaryTimeout = null)
    {
        var options = new GeminiOptions { ApiKey = apiKey, Model = "primary", FallbackModel = "backup" };
        if (primaryTimeout is { } t) options.PrimaryAttemptTimeout = t;
        var http = new HttpClient(handler) { BaseAddress = options.BaseAddress };
        return new GeminiClient(http, Options.Create(options), NullLogger<GeminiClient>.Instance);
    }

    private static HttpResponseMessage Candidate(string jsonText) => StubHandler.Json(new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["finishReason"] = "STOP",
            ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = jsonText }) },
        }),
    }.ToJsonString());

    [Fact]
    public async Task Returns_parsed_json_and_sends_key_in_header_not_url()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("""{"value":"ok"}""")));

        var answer = await Client(handler).GenerateJsonAsync<Answer>("sys", "user", Schema, TimeSpan.FromSeconds(5), default);

        Assert.Equal("ok", answer.Value);
        var request = Assert.Single(handler.Seen);
        Assert.EndsWith("models/primary:generateContent", request.RequestUri!.AbsolutePath);
        Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("test-key", request.RequestUri.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Falls_back_to_the_second_model_when_the_first_fails(HttpStatusCode status)
    {
        var handler = new StubHandler((req, _) => Task.FromResult(req.RequestUri!.AbsolutePath.Contains("primary")
            ? StubHandler.Json("""{"error":{}}""", status)
            : Candidate("""{"value":"from backup"}""")));

        var answer = await Client(handler).GenerateJsonAsync<Answer>("sys", "user", Schema, TimeSpan.FromSeconds(5), default);

        Assert.Equal("from backup", answer.Value);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_hanging_primary_model_leaves_time_for_the_backup()
    {
        var handler = new StubHandler(async (req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("primary")) await Task.Delay(TimeSpan.FromSeconds(30));
            return Candidate("""{"value":"from backup"}""");
        });

        var answer = await Client(handler, primaryTimeout: TimeSpan.FromMilliseconds(100))
            .GenerateJsonAsync<Answer>("sys", "user", Schema, TimeSpan.FromSeconds(5), default);

        Assert.Equal("from backup", answer.Value);
    }

    [Fact]
    public async Task Both_models_failing_is_LlmUnavailable()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json("{}", HttpStatusCode.ServiceUnavailable)));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Client(handler).GenerateJsonAsync<Answer>("sys", "user", Schema, TimeSpan.FromSeconds(5), default));

        Assert.Contains("primary: overloaded", ex.Message);
        Assert.Contains("backup: overloaded", ex.Message);
    }

    [Fact]
    public async Task Malformed_json_from_the_model_is_LlmUnavailable()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("not json")));

        await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Client(handler).GenerateJsonAsync<Answer>("sys", "user", Schema, TimeSpan.FromSeconds(5), default));
    }

    [Fact]
    public async Task No_api_key_fails_fast_without_calling_out()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("{}")));

        await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Client(handler, apiKey: null).GenerateJsonAsync<Answer>("sys", "user", Schema, TimeSpan.FromSeconds(5), default));
        Assert.Equal(0, handler.Calls);
    }
}
