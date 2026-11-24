using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using BookFinder.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookFinder.Infrastructure.Gemini;

/// <summary>
/// Minimal Gemini REST client for structured output: system prompt + user text in, JSON matching a schema out.
/// Tries two models inside one time budget, skipping any model the <see cref="GeminiModelGate"/> says is cooling
/// down or at our own rate cap. Every failure becomes <see cref="LlmUnavailableException"/> so callers fall back
/// to deterministic code.
/// </summary>
public sealed class GeminiClient(
    HttpClient http,
    IOptions<GeminiOptions> options,
    GeminiModelGate gate,
    TimeProvider time,
    ILogger<GeminiClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GenerateJsonAsync<T>(
        GeminiOptions.CallProfile profile, string systemPrompt, string userText, JsonObject responseSchema, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.IsConfigured) throw new LlmUnavailableException("GEMINI_API_KEY is not set.");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(profile.Budget);

        var models = Order(opts, profile);
        var body = BuildRequest(systemPrompt, userText, responseSchema);
        var failures = new List<string>();
        var attempted = 0;

        foreach (var model in models)
        {
            if (!gate.TryAcquire(model, out var skipReason))
            {
                logger.LogInformation("Gemini {Operation} {Model} skipped: {Reason}", profile.Operation, model, skipReason);
                failures.Add($"{model}: skipped ({skipReason})");
                continue;
            }

            // The first model actually called gets its share; whichever model is called last gets what's left.
            var isLastCandidate = model == models[^1];
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            if (attempted++ == 0 && !isLastCandidate) attempt.CancelAfter(profile.FirstModelTimeout);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var text = await CallAsync(model, body, opts.ApiKey!, attempt.Token);
                var result = JsonSerializer.Deserialize<T>(text, Json) ?? throw new JsonException("Gemini returned null JSON.");
                gate.ReportSuccess(model);
                logger.LogInformation("Gemini {Operation} {Model} answered in {Ms} ms", profile.Operation, model, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                gate.ReportFailure(model, GeminiModelGate.Failure.TimedOut);
                failures.Add($"{model}: timed out after {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (GeminiCallException ex)
            {
                gate.ReportFailure(model, ex.Failure, CooldownEnd(ex.Error));
                failures.Add($"{model}: {ex.Error}");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                gate.ReportFailure(model, GeminiModelGate.Failure.Other);
                failures.Add($"{model}: {ex.GetType().Name}: {ex.Message}");
            }

            logger.LogWarning("Gemini {Operation} {Model} failed after {Ms} ms: {Reason}",
                profile.Operation, model, stopwatch.ElapsedMilliseconds, failures[^1]);
            if (budget.IsCancellationRequested) break;
        }

        throw new LlmUnavailableException(string.Join("; ", failures));
    }

    private static List<string> Order(GeminiOptions opts, GeminiOptions.CallProfile profile)
    {
        var models = new[] { opts.Model, opts.FallbackModel }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!).Distinct().ToList();
        if (profile.PreferFallbackModel) models.Reverse();
        return models;
    }

    /// <summary>A daily quota resets at midnight Pacific; otherwise use Google's retry delay if it gave one.</summary>
    private DateTimeOffset? CooldownEnd(GeminiError error)
    {
        var now = time.GetUtcNow();
        if (error.IsDailyQuota) return NextPacificMidnight(now);
        return error.RetryDelay is { } delay ? now + delay : null;
    }

    public static DateTimeOffset NextPacificMidnight(DateTimeOffset now)
    {
        try
        {
            var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
            var local = TimeZoneInfo.ConvertTime(now, pacific);
            var midnight = local.Date.AddDays(1);
            return new DateTimeOffset(midnight, pacific.GetUtcOffset(midnight));
        }
        catch (TimeZoneNotFoundException)
        {
            return now.AddHours(1); // no tz data: retry hourly rather than give up for the day
        }
    }

    private static JsonObject BuildRequest(string systemPrompt, string userText, JsonObject schema) => new()
    {
        ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = systemPrompt }) },
        ["contents"] = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = userText }),
        }),
        ["generationConfig"] = new JsonObject
        {
            ["temperature"] = 0,
            ["responseMimeType"] = "application/json",
            ["responseJsonSchema"] = schema.DeepClone(),
        },
    };

    private async Task<string> CallAsync(string model, JsonObject body, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = GeminiError.Parse((int)response.StatusCode, payload);
            var failure = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => GeminiModelGate.Failure.RateLimited,
                HttpStatusCode.ServiceUnavailable or HttpStatusCode.InternalServerError or HttpStatusCode.GatewayTimeout => GeminiModelGate.Failure.Overloaded,
                _ => GeminiModelGate.Failure.Other,
            };
            throw new GeminiCallException(failure, error);
        }

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new GeminiCallException(GeminiModelGate.Failure.Other, new GeminiError(200, null, "no candidates (blocked?)", null, null));

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() is { } reason && reason != "STOP")
            throw new GeminiCallException(GeminiModelGate.Failure.Other, new GeminiError(200, null, $"finish reason {reason}", null, null));

        // Join the answer parts, skipping any "thought" parts from thinking models.
        var text = string.Concat(candidate.GetProperty("content").GetProperty("parts").EnumerateArray()
            .Where(p => !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null));

        return string.IsNullOrWhiteSpace(text)
            ? throw new GeminiCallException(GeminiModelGate.Failure.Other, new GeminiError(200, null, "empty response", null, null))
            : text;
    }

    private sealed class GeminiCallException(GeminiModelGate.Failure failure, GeminiError error) : Exception(error.ToString())
    {
        public GeminiModelGate.Failure Failure { get; } = failure;

        public GeminiError Error { get; } = error;
    }
}
