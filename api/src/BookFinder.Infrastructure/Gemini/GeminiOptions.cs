namespace BookFinder.Infrastructure.Gemini;

public sealed class GeminiOptions
{
    public Uri BaseAddress { get; set; } = new("https://generativelanguage.googleapis.com/v1beta/");

    public string? ApiKey { get; set; }

    /// <summary>
    /// Tried first. Chosen by measurement on Nov 24, 2026 (free tier): Flash-Lite answered in ~1 s and read every
    /// golden query correctly, while gemini-3.8-flash timed out, returned 503 or was rate-limited on every call.
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Tried when the primary model is overloaded, rate-limited or slow. A different model has its own quota.</summary>
    public string? FallbackModel { get; set; } = "gemini-3.1-flash-lite";

    /// <summary>Total time for parsing, across both models, before the rule-based parser takes over.</summary>
    public TimeSpan ParseBudget { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Total time for explanations, across both models, before templates take over. Shorter than parsing:
    /// the templates are a good fallback, so a slow model isn't worth waiting for.</summary>
    public TimeSpan ExplainBudget { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Longest the primary model gets before we move on to the fallback model. Overloaded models sometimes
    /// hang instead of returning 503, and the fallback needs time left in the budget.
    /// </summary>
    public TimeSpan PrimaryAttemptTimeout { get; set; } = TimeSpan.FromSeconds(4);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
