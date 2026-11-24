using BookFinder.Application.Ports;
using BookFinder.Domain;

namespace BookFinder.Tests.Support;

/// <summary>Stands in for Gemini: returns fixed hypotheses, or throws to force the rule-based fallback.</summary>
public sealed class FakeLlmParser(Func<string, IReadOnlyList<QueryHypothesis>>? parse = null) : ILlmQueryParser
{
    public static FakeLlmParser Unavailable => new(_ => throw new LlmUnavailableException("test: LLM down"));

    public static FakeLlmParser Returning(params QueryHypothesis[] hypotheses) => new(_ => hypotheses);

    public Task<IReadOnlyList<QueryHypothesis>> ParseAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult((parse ?? (_ => []))(query));
}

/// <summary>Explanation writer that's always down, so tests see the deterministic templates.</summary>
public sealed class UnavailableExplainer : IExplanationWriter
{
    public Task<IReadOnlyDictionary<string, string>> WriteAsync(string query, IReadOnlyList<RankedCandidate> candidates, CancellationToken cancellationToken) =>
        throw new LlmUnavailableException("test: LLM down");
}

/// <summary>Explanation writer that echoes a fixed sentence per work.</summary>
public sealed class EchoExplainer : IExplanationWriter
{
    public Task<IReadOnlyDictionary<string, string>> WriteAsync(string query, IReadOnlyList<RankedCandidate> candidates, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(candidates.ToDictionary(c => c.Book.WorkKey, c => $"LLM says {c.Book.Title}."));
}

public static class H
{
    public static QueryHypothesis Hyp(string? title, string? author, int? year = null, bool series = false) =>
        new(title, author, [], year, IsSeries: series);
}
