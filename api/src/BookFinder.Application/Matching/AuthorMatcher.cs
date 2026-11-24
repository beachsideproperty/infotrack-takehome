using BookFinder.Application.Text;

namespace BookFinder.Application.Matching;

/// <summary>
/// Loose person-name matching: "Tolkien", "J.R.R. Tolkien" and "John Ronald Reuel Tolkien" all match
/// "J. R. R. Tolkien"; "Charles Dickens" does not match "Monica Dickens".
/// </summary>
public static class AuthorMatcher
{
    public static bool Matches(string? query, string? candidate)
    {
        var q = NameParts.From(query);
        var c = NameParts.From(candidate);
        if (q is null || c is null) return false;
        if (q.Last != c.Last) return false;

        // A bare surname ("tolkien") matches any first name.
        if (q.Given.Length == 0 || c.Given.Length == 0) return true;

        // Compare given names pairwise; initials match full names that start with them.
        var pairs = Math.Min(q.Given.Length, c.Given.Length);
        for (var i = 0; i < pairs; i++)
        {
            if (!GivenNamesCompatible(q.Given[i], c.Given[i])) return false;
        }
        return true;
    }

    private static bool GivenNamesCompatible(string a, string b) =>
        a == b || (a.Length == 1 && b.StartsWith(a, StringComparison.Ordinal)) || (b.Length == 1 && a.StartsWith(b, StringComparison.Ordinal));

    private sealed record NameParts(string[] Given, string Last)
    {
        public static NameParts? From(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // "Tolkien, J. R. R." → "J. R. R. Tolkien"
            var comma = raw.IndexOf(',');
            if (comma > 0 && comma < raw.Length - 1) raw = $"{raw[(comma + 1)..]} {raw[..comma]}";

            var tokens = TextNormalizer.Tokens(raw);
            if (tokens.Length == 0) return null;

            // Initials written together ("jrr") are rare; keep tokens as-is otherwise.
            return new NameParts(tokens[..^1], tokens[^1]);
        }
    }
}
