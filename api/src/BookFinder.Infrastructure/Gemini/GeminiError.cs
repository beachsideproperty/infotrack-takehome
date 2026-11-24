using System.Globalization;
using System.Text.Json;

namespace BookFinder.Infrastructure.Gemini;

/// <summary>
/// What Google says when a call fails: status, message and, for 429s, which quota and how long to wait.
/// Parsed from the error body (google.rpc QuotaFailure and RetryInfo details); everything is optional.
/// </summary>
public sealed record GeminiError(int HttpStatus, string? Status, string? Message, string? QuotaId, TimeSpan? RetryDelay)
{
    public bool IsDailyQuota => QuotaId?.Contains("PerDay", StringComparison.OrdinalIgnoreCase) == true;

    public static GeminiError Parse(int httpStatus, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new GeminiError(httpStatus, null, null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return new GeminiError(httpStatus, null, null, null, null);

            string? quotaId = null;
            TimeSpan? retryDelay = null;
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                    {
                        quotaId ??= violations.EnumerateArray()
                            .Select(v => v.TryGetProperty("quotaId", out var q) ? q.GetString() : null)
                            .FirstOrDefault(q => q is not null);
                    }
                    if (detail.TryGetProperty("retryDelay", out var delay) && delay.GetString() is { } text)
                        retryDelay = ParseDuration(text);
                }
            }

            return new GeminiError(
                httpStatus,
                error.TryGetProperty("status", out var s) ? s.GetString() : null,
                error.TryGetProperty("message", out var m) ? m.GetString() : null,
                quotaId,
                retryDelay);
        }
        catch (JsonException)
        {
            return new GeminiError(httpStatus, null, null, null, null);
        }
    }

    /// <summary>Protobuf duration: "25s", "1.5s".</summary>
    private static TimeSpan? ParseDuration(string text) =>
        text.EndsWith('s') && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    /// <summary>One line for logs: no prompt, no key.</summary>
    public override string ToString()
    {
        var parts = new List<string> { $"HTTP {HttpStatus}" };
        if (Status is not null) parts.Add(Status);
        if (QuotaId is not null) parts.Add($"quota {QuotaId}");
        if (RetryDelay is { } d) parts.Add($"retry after {d.TotalSeconds:0.#}s");
        if (Message is not null) parts.Add($"\"{(Message.Length > 160 ? Message[..160] + "…" : Message)}\"");
        return string.Join(", ", parts);
    }
}
