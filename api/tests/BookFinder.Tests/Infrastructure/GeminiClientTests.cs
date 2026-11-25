using System.Net;
using System.Text.Json.Nodes;
using BookFinder.Application.Ports;
using BookFinder.Domain;
using BookFinder.Infrastructure.Gemini;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BookFinder.Tests.Infrastructure;

public class GeminiClientTests
{
    private static readonly JsonObject Schema = new() { ["type"] = "object" };

    private static readonly GeminiOptions.CallProfile Parse = new("parse", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), PreferFallbackModel: false);

    private static readonly GeminiOptions.CallProfile Explain = Parse with { Operation = "explain", PreferFallbackModel = true };

    private sealed record Answer(string Value);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 20, 0, 0, TimeSpan.Zero));

    private (GeminiClient Client, GeminiModelGate Gate) Create(StubHandler handler, string? apiKey = "test-key", int rpm = 14)
    {
        var options = Options.Create(new GeminiOptions { ApiKey = apiKey, Model = "primary", FallbackModel = "backup", RequestsPerMinutePerModel = rpm });
        var gate = new GeminiModelGate(options, _time);
        var http = new HttpClient(handler) { BaseAddress = options.Value.BaseAddress };
        return (new GeminiClient(http, options, gate, _time, NullLogger<GeminiClient>.Instance), gate);
    }

    private static Task<Answer> Generate(GeminiClient client, GeminiOptions.CallProfile? profile = null) =>
        client.GenerateJsonAsync<Answer>(profile ?? Parse, "sys", "user", Schema, default);

    private static HttpResponseMessage Candidate(string jsonText) => StubHandler.Json(new JsonObject
    {
        ["candidates"] = new JsonArray(new JsonObject
        {
            ["finishReason"] = "STOP",
            ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = jsonText }) },
        }),
    }.ToJsonString());

    private static HttpResponseMessage RateLimited(string quotaId, string retryDelay) => StubHandler.Json($$$"""
        {"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"You exceeded your current quota.",
          "details":[
            {"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaMetric":"generativelanguage.googleapis.com/generate_content_free_tier_requests","quotaId":"{{{quotaId}}}"}]},
            {"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"{{{retryDelay}}}"}]}}
        """, HttpStatusCode.TooManyRequests);

    private static bool Calls(HttpRequestMessage r, string model) => r.RequestUri!.AbsolutePath.Contains($"/{model}:");

    [Fact]
    public async Task Returns_parsed_json_and_sends_key_in_header_not_url()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("""{"value":"ok"}""")));

        var answer = await Generate(Create(handler).Client);

        Assert.Equal("ok", answer.Value);
        var request = Assert.Single(handler.Seen);
        Assert.EndsWith("models/primary:generateContent", request.RequestUri!.AbsolutePath);
        Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("test-key", request.RequestUri.ToString());
    }

    [Fact]
    public async Task Explanations_start_on_the_other_model_to_spread_quota()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("""{"value":"ok"}""")));

        await Generate(Create(handler).Client, Explain);

        Assert.True(Calls(Assert.Single(handler.Seen), "backup"));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Falls_back_to_the_second_model_when_the_first_fails(HttpStatusCode status)
    {
        var handler = new StubHandler((req, _) => Task.FromResult(Calls(req, "primary")
            ? StubHandler.Json("""{"error":{}}""", status)
            : Candidate("""{"value":"from backup"}""")));

        var answer = await Generate(Create(handler).Client);

        Assert.Equal("from backup", answer.Value);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_hanging_first_model_leaves_time_for_the_second()
    {
        var handler = new StubHandler(async (req, _) =>
        {
            if (Calls(req, "primary")) await Task.Delay(TimeSpan.FromSeconds(30));
            return Candidate("""{"value":"from backup"}""");
        });

        var answer = await Create(handler).Client.GenerateJsonAsync<Answer>(
            Parse with { FirstModelTimeout = TimeSpan.FromMilliseconds(100) }, "sys", "user", Schema, default);

        Assert.Equal("from backup", answer.Value);
    }

    [Fact]
    public async Task An_overloaded_model_is_skipped_without_a_call_until_its_cooldown_ends()
    {
        var handler = new StubHandler((req, _) => Task.FromResult(Calls(req, "primary")
            ? StubHandler.Json("""{"error":{"code":503,"status":"UNAVAILABLE","message":"high demand"}}""", HttpStatusCode.ServiceUnavailable)
            : Candidate("""{"value":"ok"}""")));
        var (client, _) = Create(handler);

        await Generate(client);                                  // primary 503 → backup
        await Generate(client);                                  // primary skipped → backup only
        Assert.Equal(1, handler.Seen.Count(r => Calls(r, "primary")));

        _time.Advance(TimeSpan.FromSeconds(21));                 // overload cooldown is 20 s
        await Generate(client);
        Assert.Equal(2, handler.Seen.Count(r => Calls(r, "primary")));
    }

    [Fact]
    public async Task A_rate_limited_model_waits_for_googles_retry_delay()
    {
        var handler = new StubHandler((req, _) => Task.FromResult(Calls(req, "primary")
            ? RateLimited("GenerateRequestsPerMinutePerProjectPerModel-FreeTier", "37s")
            : Candidate("""{"value":"ok"}""")));
        var (client, gate) = Create(handler);

        await Generate(client);

        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(gate.TryAcquire("primary", out var reason));
        Assert.Contains("rate limit", reason);
        _time.Advance(TimeSpan.FromSeconds(8));
        Assert.True(gate.TryAcquire("primary", out _));
    }

    [Fact]
    public async Task A_daily_quota_benches_the_model_until_midnight_pacific()
    {
        var handler = new StubHandler((req, _) => Task.FromResult(Calls(req, "primary")
            ? RateLimited("GenerateRequestsPerDayPerProjectPerModel-FreeTier", "20s")
            : Candidate("""{"value":"ok"}""")));
        var (client, gate) = Create(handler);

        await Generate(client); // 20:00 UTC = 12:00 Pacific

        _time.Advance(TimeSpan.FromHours(11));
        Assert.False(gate.TryAcquire("primary", out _));        // 23:00 Pacific
        _time.Advance(TimeSpan.FromHours(1.1));
        Assert.True(gate.TryAcquire("primary", out _));         // past midnight Pacific
    }

    [Fact]
    public async Task Our_own_per_minute_cap_routes_to_the_other_model_before_google_says_no()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("""{"value":"ok"}""")));
        var (client, _) = Create(handler, rpm: 2);

        for (var i = 0; i < 4; i++) await Generate(client);

        Assert.Equal(2, handler.Seen.Count(r => Calls(r, "primary")));
        Assert.Equal(2, handler.Seen.Count(r => Calls(r, "backup")));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(client)); // both at cap: no call at all
        Assert.Equal(4, handler.Calls);

        _time.Advance(TimeSpan.FromMinutes(1));
        await Generate(client);
        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public async Task Both_models_failing_is_LlmUnavailable_with_googles_reasons()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(
            StubHandler.Json("""{"error":{"code":503,"status":"UNAVAILABLE","message":"high demand"}}""", HttpStatusCode.ServiceUnavailable)));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler).Client));

        Assert.Equal(LlmFailureReason.Overloaded, ex.Reason);
        Assert.StartsWith("overloaded", ex.Message);
        Assert.Contains("primary: overloaded: HTTP 503, UNAVAILABLE", ex.Message);
        Assert.Contains("backup: overloaded: HTTP 503, UNAVAILABLE", ex.Message);
    }

    [Fact]
    public async Task Daily_quota_on_both_models_is_reported_as_daily_quota_and_stays_that_way_while_skipped()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(RateLimited("GenerateRequestsPerDayPerProjectPerModel-FreeTier", "20s")));
        var (client, _) = Create(handler);

        var first = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(client));
        var second = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(client)); // both skipped, no calls

        Assert.Equal(LlmFailureReason.DailyQuota, first.Reason);
        Assert.Equal(LlmFailureReason.DailyQuota, second.Reason);
        Assert.Contains("daily quota", second.Message);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Mixed_failures_report_the_one_that_clears_soonest()
    {
        var handler = new StubHandler((req, _) => Task.FromResult(Calls(req, "primary")
            ? RateLimited("GenerateRequestsPerDayPerProjectPerModel-FreeTier", "20s")
            : StubHandler.Json("""{"error":{"code":503,"status":"UNAVAILABLE"}}""", HttpStatusCode.ServiceUnavailable)));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler).Client));

        Assert.Equal(LlmFailureReason.Overloaded, ex.Reason); // "try again in a minute" beats "try tomorrow"
    }

    [Fact]
    public async Task A_per_minute_429_is_rate_limited_and_our_own_cap_is_too()
    {
        var limited = new StubHandler((_, _) => Task.FromResult(RateLimited("GenerateRequestsPerMinutePerProjectPerModel-FreeTier", "30s")));
        var google = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(limited).Client));

        var ok = new StubHandler((_, _) => Task.FromResult(Candidate("""{"value":"ok"}""")));
        var (client, _) = Create(ok, rpm: 1);
        await Generate(client);
        await Generate(client);
        var ours = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(client));

        Assert.Equal(LlmFailureReason.RateLimited, google.Reason);
        Assert.Equal(LlmFailureReason.RateLimited, ours.Reason);
    }

    [Fact]
    public async Task Malformed_json_from_the_model_is_a_bad_answer()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("not json")));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler).Client));
        Assert.Equal(LlmFailureReason.BadAnswer, ex.Reason);
        Assert.StartsWith("bad-answer", ex.Message);
    }

    [Fact]
    public async Task A_blocked_or_cut_off_answer_is_a_bad_answer()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(
            """{"candidates":[{"finishReason":"SAFETY","content":{"parts":[]}}]}""")));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler).Client));
        Assert.Equal(LlmFailureReason.BadAnswer, ex.Reason);
        Assert.Contains("finish reason SAFETY", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_ARGUMENT")]   // e.g. "API key not valid"
    [InlineData(HttpStatusCode.Forbidden, "PERMISSION_DENIED")]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND")]            // a retired model name
    public async Task A_refused_request_is_rejected(HttpStatusCode status, string googleStatus)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(StubHandler.Json(
            $$$"""{"error":{"code":{{{(int)status}}},"status":"{{{googleStatus}}}","message":"no"}}""", status)));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler).Client));
        Assert.Equal(LlmFailureReason.Rejected, ex.Reason);
        Assert.Contains($"primary: rejected: HTTP {(int)status}, {googleStatus}", ex.Message);
    }

    [Fact]
    public async Task No_connection_to_google_is_unreachable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Name or service not known"));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler).Client));
        Assert.Equal(LlmFailureReason.Unreachable, ex.Reason);
        Assert.Contains("Name or service not known", ex.Message);
    }

    [Fact]
    public void A_refused_request_is_reported_last_before_the_daily_quota_because_retrying_soon_wont_fix_it() =>
        Assert.Equal(LlmFailureReason.BadAnswer, GeminiClient.Overall([GeminiModelGate.Failure.Rejected, GeminiModelGate.Failure.BadAnswer]));

    [Fact]
    public async Task No_api_key_fails_fast_without_calling_out()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Candidate("{}")));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => Generate(Create(handler, apiKey: null).Client));
        Assert.Equal(LlmFailureReason.NotConfigured, ex.Reason);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Parses_quota_and_retry_delay_from_googles_error_body()
    {
        var error = GeminiError.Parse(429, """
            {"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"Quota exceeded",
              "details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]},
                         {"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"12.5s"}]}}
            """);

        Assert.Equal("RESOURCE_EXHAUSTED", error.Status);
        Assert.True(error.IsDailyQuota);
        Assert.Equal(TimeSpan.FromSeconds(12.5), error.RetryDelay);
        Assert.Equal("HTTP 429, RESOURCE_EXHAUSTED, quota GenerateRequestsPerDayPerProjectPerModel-FreeTier, retry after 12.5s, \"Quota exceeded\"", error.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>gateway error</html>")]
    public void Unparseable_error_bodies_still_give_the_status(string? body) =>
        Assert.Equal("HTTP 502", GeminiError.Parse(502, body).ToString());

    [Fact]
    public void Next_pacific_midnight_handles_daylight_saving()
    {
        // Nov 24, 2026 is PST (UTC-8): midnight Pacific = 08:00 UTC next day.
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero), GeminiClient.NextPacificMidnight(new DateTimeOffset(2026, 11, 24, 20, 0, 0, TimeSpan.Zero)));
        // Jul 1 is PDT (UTC-7): midnight Pacific = 07:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 7, 0, 0, TimeSpan.Zero), GeminiClient.NextPacificMidnight(new DateTimeOffset(2026, 7, 1, 20, 0, 0, TimeSpan.Zero)));
    }
}
