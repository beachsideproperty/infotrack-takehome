namespace BookFinder.Domain;

public enum ParserKind
{
    Llm,
    Fallback,
}

/// <summary>Why the LLM couldn't answer, in terms a user can act on.</summary>
public enum LlmFailureReason
{
    /// <summary>Something else went wrong (bad answer, unexpected HTTP status).</summary>
    Error,
    NotConfigured,
    /// <summary>Google answered 503/500 ("high demand"). Usually clears within a minute.</summary>
    Overloaded,
    TimedOut,
    /// <summary>A per-minute limit: Google's 429, or our own cap that keeps us under it.</summary>
    RateLimited,
    /// <summary>The free tier's daily quota is used up; it resets at midnight Pacific.</summary>
    DailyQuota,
}

/// <summary>Hypotheses ordered from most to least likely.</summary>
/// <param name="LlmFailure">Why the LLM wasn't used, when <see cref="Parser"/> is the fallback.</param>
public sealed record ParsedQuery(IReadOnlyList<QueryHypothesis> Hypotheses, ParserKind Parser, LlmFailureReason? LlmFailure = null);
