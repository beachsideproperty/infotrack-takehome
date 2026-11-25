using Microsoft.Extensions.Options;

namespace BookFinder.Infrastructure.Gemini;

/// <summary>
/// Process-wide guard in front of each Gemini model, so we stay inside the free tier instead of discovering
/// its limits through errors:
/// <list type="bullet">
/// <item>our own requests-per-minute cap per model (a little under Google's), so we never cause a 429;</item>
/// <item>a cooldown after a model fails (429 → Google's retry delay or the daily reset; 503 or timeout → a few
/// seconds), during which calls skip that model instantly instead of waiting on it.</item>
/// </list>
/// </summary>
public sealed class GeminiModelGate(IOptions<GeminiOptions> options, TimeProvider time)
{
    private readonly Dictionary<string, ModelState> _models = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public enum Failure
    {
        Overloaded,
        TimedOut,
        RateLimited,
        DailyQuota,
        Other,
    }

    /// <summary>Reserves one request on <paramref name="model"/>, or says why it can't be used right now.</summary>
    public bool TryAcquire(string model, out string? reason) => TryAcquire(model, out reason, out _);

    /// <param name="blockedBy">When skipped: the failure behind the cooldown, or <see cref="Failure.RateLimited"/> for our own cap.</param>
    public bool TryAcquire(string model, out string? reason, out Failure? blockedBy)
    {
        var now = time.GetUtcNow();
        lock (_lock)
        {
            var state = State(model);
            if (state.CoolingUntil > now)
            {
                reason = $"cooling down {(state.CoolingUntil - now).TotalSeconds:0}s after {Describe(state.CoolingBecause)}";
                blockedBy = state.CoolingBecause;
                return false;
            }

            while (state.Recent.Count > 0 && now - state.Recent.Peek() >= TimeSpan.FromMinutes(1)) state.Recent.Dequeue();
            if (state.Recent.Count >= options.Value.RequestsPerMinutePerModel)
            {
                reason = $"local limit of {options.Value.RequestsPerMinutePerModel} requests/minute reached";
                blockedBy = Failure.RateLimited;
                return false;
            }

            state.Recent.Enqueue(now);
            reason = null;
            blockedBy = null;
            return true;
        }
    }

    /// <summary>Puts a model on cooldown after a failure. <paramref name="until"/> overrides the default length.</summary>
    public void ReportFailure(string model, Failure failure, DateTimeOffset? until = null)
    {
        var o = options.Value;
        var length = failure switch
        {
            Failure.RateLimited => TimeSpan.FromSeconds(60),
            Failure.DailyQuota => TimeSpan.FromHours(1), // normally overridden by the reset time
            Failure.Overloaded => o.OverloadedCooldown,
            Failure.TimedOut => o.TimeoutCooldown,
            _ => TimeSpan.Zero,
        };
        var cooldownEnd = until ?? time.GetUtcNow() + length;

        lock (_lock)
        {
            var state = State(model);
            if (cooldownEnd > state.CoolingUntil)
            {
                state.CoolingUntil = cooldownEnd;
                state.CoolingBecause = failure;
            }
        }
    }

    public void ReportSuccess(string model)
    {
        lock (_lock) State(model).CoolingUntil = DateTimeOffset.MinValue;
    }

    private static string Describe(Failure failure) => failure switch
    {
        Failure.RateLimited => "a rate limit",
        Failure.DailyQuota => "the daily quota ran out",
        Failure.Overloaded => "an overload",
        Failure.TimedOut => "a timeout",
        _ => "an error",
    };

    private ModelState State(string model)
    {
        if (!_models.TryGetValue(model, out var state)) _models[model] = state = new ModelState();
        return state;
    }

    private sealed class ModelState
    {
        public Queue<DateTimeOffset> Recent { get; } = new();

        public DateTimeOffset CoolingUntil { get; set; } = DateTimeOffset.MinValue;

        public Failure CoolingBecause { get; set; } = Failure.Other;
    }
}
