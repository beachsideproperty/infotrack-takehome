using System.Text.RegularExpressions;
using BookFinder.Application.Text;

namespace BookFinder.Application.Matching;

/// <summary>
/// Spots box sets, omnibuses and multi-title volumes ("Harry Potter (series) 1-7", "Novels (Emma / Persuasion)").
/// The brief asks for works, so these never outrank a single book.
/// </summary>
public static partial class CollectionDetector
{
    private static readonly string[] Markers =
    [
        "collection", "box set", "boxed set", "boxset", "omnibus", "complete works", "complete novels",
        "collected works", "trilogy", "bundle", "books 1", "novels",
    ];

    public static bool IsCollection(string title)
    {
        if (title.Contains(" / ", StringComparison.Ordinal) || title.Contains("[collection", StringComparison.OrdinalIgnoreCase))
            return true;

        // "(series)" or a title ending in "series", but not "A Series of Unfortunate Events".
        var normalized = TextNormalizer.Normalize(title);
        if (title.Contains("(series)", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(" series", StringComparison.Ordinal))
            return true;

        var padded = $" {normalized} ";
        return Markers.Any(m => padded.Contains($" {m} ", StringComparison.Ordinal)) || NumberRange().IsMatch(title);
    }

    [GeneratedRegex(@"\b\d+\s*-\s*\d+\b")]
    private static partial Regex NumberRange();
}
