namespace BookFinder.Infrastructure.Gemini;

public sealed class GeminiOptions
{
    public Uri BaseAddress { get; set; } = new("https://generativelanguage.googleapis.com/v1beta/");

    public string? ApiKey { get; set; }

    /// <summary>
    /// Parsing tries this first; explanations try it second. Chosen by measurement on Nov 24, 2026 (free tier):
    /// Flash-Lite answered in ~1 s and read every golden query correctly, while gemini-3.8-flash timed out,
    /// returned 503 or was rate-limited on nearly every call.
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>
    /// Explanations try this first; parsing falls back to it. Splitting the two calls of a search across two
    /// models doubles the free-tier headroom, since each model has its own per-minute quota.
    /// </summary>
    public string? FallbackModel { get; set; } = "gemini-3.1-flash-lite";

    /// <summary>Our own cap per model. Google's free tier allows 15 for Flash-Lite; one spare absorbs clock skew.</summary>
    public int RequestsPerMinutePerModel { get; set; } = 14;

    /// <summary>Parsing matters most (it decides what we search for), so it gets the longest budget.</summary>
    public CallProfile Parse { get; set; } = new("parse", Budget: TimeSpan.FromSeconds(9), FirstModelTimeout: TimeSpan.FromSeconds(5.5), PreferFallbackModel: false);

    /// <summary>Templates are a good fallback, so explanations get less time.</summary>
    public CallProfile Explain { get; set; } = new("explain", Budget: TimeSpan.FromSeconds(6), FirstModelTimeout: TimeSpan.FromSeconds(4), PreferFallbackModel: true);

    /// <summary>How long a model is skipped after a 503 "overloaded".</summary>
    public TimeSpan OverloadedCooldown { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How long a model is skipped after it didn't answer in time.</summary>
    public TimeSpan TimeoutCooldown { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <param name="Operation">For logs.</param>
    /// <param name="Budget">Total time across both models before the deterministic fallback takes over.</param>
    /// <param name="FirstModelTimeout">The first model's share, leaving the rest for the second (overloaded models can hang).</param>
    /// <param name="PreferFallbackModel">Try <see cref="FallbackModel"/> first, to spread quota.</param>
    public sealed record CallProfile(string Operation, TimeSpan Budget, TimeSpan FirstModelTimeout, bool PreferFallbackModel);
}
