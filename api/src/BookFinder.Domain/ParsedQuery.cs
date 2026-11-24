namespace BookFinder.Domain;

public enum ParserKind
{
    Llm,
    Fallback,
}

/// <summary>Hypotheses ordered from most to least likely.</summary>
public sealed record ParsedQuery(IReadOnlyList<QueryHypothesis> Hypotheses, ParserKind Parser);
