using BookFinder.Application.Text;

namespace BookFinder.Application.Parsing;

/// <summary>Words that never help find a work: edition descriptions and filler.</summary>
public static class QueryWords
{
    /// <summary>Describe an edition, not a work.</summary>
    public static readonly IReadOnlySet<string> EditionWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "illustrated", "deluxe", "edition", "hardcover", "hardback", "paperback", "ebook", "kindle", "audiobook",
        "unabridged", "abridged", "annotated", "anniversary", "collectors", "collector", "special", "boxed", "box", "set",
    };

    public static readonly IReadOnlySet<string> Filler = new HashSet<string>(StringComparer.Ordinal)
    {
        "book", "books", "novel", "by", "that", "about", "called", "titled", "written", "the", "a", "an", "and", "of",
    };

    /// <summary>Normalized keywords worth searching on: "Whale", "illustrated", "the" → "whale".</summary>
    public static IReadOnlyList<string> Meaningful(IEnumerable<string> keywords) => keywords
        .SelectMany(TextNormalizer.Tokens)
        .Where(t => !EditionWords.Contains(t) && !Filler.Contains(t))
        .Distinct(StringComparer.Ordinal)
        .ToList();
}
