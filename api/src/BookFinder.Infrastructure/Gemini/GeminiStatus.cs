using Microsoft.Extensions.Options;

namespace BookFinder.Infrastructure.Gemini;

/// <summary>
/// Gemini's availability as the gate currently sees it: which models are cooling down and why. Read-only and
/// free: it never calls Google, so it can sit behind <c>/health</c>. It reflects the last failures, not a live
/// probe, so "ok" means "nothing has failed recently", not "the next call will succeed".
/// </summary>
public sealed class GeminiStatus(IOptions<GeminiOptions> options, GeminiModelGate gate)
{
    /// <param name="Status">"ok" | "limited" (one model resting) | "unavailable" (all resting) | "notConfigured".</param>
    public sealed record Report(string Status, IReadOnlyList<ModelReport> Models);

    /// <param name="Issue">Same vocabulary as a search's <c>aiIssue</c>; null when the model is available.</param>
    public sealed record ModelReport(string Model, bool Available, string? Issue, int? RetryInSeconds, int RequestsLastMinute);

    public Report Current()
    {
        var opts = options.Value;
        if (!opts.IsConfigured) return new Report("notConfigured", []);

        var models = new[] { opts.Model, opts.FallbackModel }
            .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!).Distinct()
            .Select(m => gate.Snapshot(m))
            .Select(s => new ModelReport(
                s.Model,
                s.BlockedBy is null,
                s.BlockedBy is { } f ? Issue(f) : null,
                s.AvailableIn is { } wait ? (int)Math.Ceiling(wait.TotalSeconds) : null,
                s.RequestsLastMinute))
            .ToList();

        var available = models.Count(m => m.Available);
        var status = available == models.Count ? "ok" : available == 0 ? "unavailable" : "limited";
        return new Report(status, models);
    }

    private static string Issue(GeminiModelGate.Failure failure) => failure switch
    {
        GeminiModelGate.Failure.Overloaded => "overloaded",
        GeminiModelGate.Failure.TimedOut => "timedOut",
        GeminiModelGate.Failure.RateLimited => "rateLimited",
        GeminiModelGate.Failure.DailyQuota => "dailyQuota",
        GeminiModelGate.Failure.Unreachable => "unreachable",
        GeminiModelGate.Failure.Rejected => "rejected",
        GeminiModelGate.Failure.BadAnswer => "badAnswer",
        _ => "error",
    };
}
