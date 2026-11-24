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
/// Tries the primary model, then the fallback model, inside one time budget. Every failure becomes
/// <see cref="LlmUnavailableException"/> so callers can fall back to deterministic code.
/// </summary>
public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GenerateJsonAsync<T>(
        string systemPrompt, string userText, JsonObject responseSchema, TimeSpan budget, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.IsConfigured) throw new LlmUnavailableException("GEMINI_API_KEY is not set.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);

        var models = new[] { opts.Model, opts.FallbackModel }.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        var body = BuildRequest(systemPrompt, userText, responseSchema);
        var failures = new List<string>();

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i]!;
            var isLast = i == models.Count - 1;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            if (!isLast) attempt.CancelAfter(opts.PrimaryAttemptTimeout);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var text = await CallAsync(model, body, opts.ApiKey!, attempt.Token);
                logger.LogInformation("Gemini {Model} answered in {Ms} ms", model, stopwatch.ElapsedMilliseconds);
                return JsonSerializer.Deserialize<T>(text, Json)
                    ?? throw new JsonException("Gemini returned null JSON.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                failures.Add($"{model}: timed out after {stopwatch.ElapsedMilliseconds} ms");
                if (timeout.IsCancellationRequested) break; // whole budget spent
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or GeminiResponseException)
            {
                failures.Add($"{model}: {ex.Message}");
            }
            logger.LogInformation("Gemini {Model} failed: {Reason}", model, failures[^1]);
        }

        throw new LlmUnavailableException(string.Join("; ", failures));
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
            var reason = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "rate-limited",
                HttpStatusCode.ServiceUnavailable => "overloaded",
                _ => $"HTTP {(int)response.StatusCode}",
            };
            throw new GeminiResponseException(reason);
        }

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            throw new GeminiResponseException("no candidates (blocked?)");

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() is { } reasonText && reasonText != "STOP")
            throw new GeminiResponseException($"finish reason {reasonText}");

        // Join the answer parts, skipping any "thought" parts from thinking models.
        var text = string.Concat(candidate.GetProperty("content").GetProperty("parts").EnumerateArray()
            .Where(p => !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null));

        return string.IsNullOrWhiteSpace(text) ? throw new GeminiResponseException("empty response") : text;
    }

    private sealed class GeminiResponseException(string message) : Exception(message);
}
