using BookFinder.Domain;

namespace BookFinder.Application.Ports;

/// <summary>Turns the raw query into 0–3 hypotheses, most likely first. Empty means "not a book query".</summary>
public interface ILlmQueryParser
{
    /// <exception cref="LlmUnavailableException">Not configured, timed out, rate-limited or returned junk.</exception>
    Task<IReadOnlyList<QueryHypothesis>> ParseAsync(string query, CancellationToken cancellationToken);
}

/// <summary>Writes one grounded "why this book" sentence per candidate, keyed by work key.</summary>
public interface IExplanationWriter
{
    /// <exception cref="LlmUnavailableException">Not configured, timed out, rate-limited or returned junk.</exception>
    Task<IReadOnlyDictionary<string, string>> WriteAsync(string query, IReadOnlyList<RankedCandidate> candidates, CancellationToken cancellationToken);
}

public sealed class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
