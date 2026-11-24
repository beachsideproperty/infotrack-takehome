namespace BookFinder.Application.Text;

/// <summary>
/// Forms a title might be typed in: "The Hobbit, or There and Back Again" is also "The Hobbit";
/// "Dune: Deluxe Edition" is also "Dune". All results are normalized (see <see cref="TextNormalizer.NormalizeTitle"/>).
/// </summary>
public static class TitleVariants
{
    private static readonly string[] Separators = [":", ";", ", or ", " or, ", " (", " - ", " — "];

    public static IReadOnlyCollection<string> For(string title, string? subtitle = null)
    {
        var variants = new HashSet<string>(StringComparer.Ordinal);
        Add(variants, title);

        foreach (var separator in Separators)
        {
            var index = title.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0) Add(variants, title[..index]);
        }

        if (!string.IsNullOrWhiteSpace(subtitle)) Add(variants, $"{title} {subtitle}");

        return variants;
    }

    /// <summary>
    /// The title before any subtitle, edition note or alternative: "Nineteen Eighty-Four (1984)" → "nineteen eighty four".
    /// Used to spot duplicate work records of one book.
    /// </summary>
    public static string Base(string title)
    {
        var cut = title.Length;
        foreach (var separator in Separators)
        {
            var index = title.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0 && index < cut) cut = index;
        }
        var normalized = TextNormalizer.NormalizeTitle(title[..cut]);
        return normalized.Length > 0 ? normalized : TextNormalizer.NormalizeTitle(title);
    }

    private static void Add(HashSet<string> set, string raw)
    {
        var normalized = TextNormalizer.NormalizeTitle(raw);
        if (normalized.Length > 0) set.Add(normalized);
    }
}
