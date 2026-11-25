using System.Globalization;
using System.Text;

namespace BookFinder.Application.Text;

/// <summary>Canonical forms for comparing titles and names: no accents, no punctuation, lowercase.</summary>
public static class TextNormalizer
{
    private static readonly string[] LeadingArticles = ["the ", "a ", "an "];

    /// <summary>"Les Misérables!" → "les miserables"; "J.R.R. Tolkien" → "j r r tolkien".</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            if (ch == '&') sb.Append(" and ");
            else if (ch is '\'' or '’' or 'ʼ') continue; // "Philosopher's" → "philosophers"
            else if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else sb.Append(' ');
        }

        return CollapseSpaces(sb.ToString());
    }

    /// <summary>
    /// Normalize, spell out numbers, then drop a leading article: "The Hobbit" → "hobbit";
    /// "1984" and "Nineteen Eighty-Four" → "nineteen eighty four", so they compare as the same title.
    /// </summary>
    public static string NormalizeTitle(string? title)
    {
        var normalized = NumberWords.SpellAll(Normalize(title));
        foreach (var article in LeadingArticles)
        {
            if (normalized.StartsWith(article, StringComparison.Ordinal) && normalized.Length > article.Length)
                return normalized[article.Length..];
        }
        return normalized;
    }

    public static string[] Tokens(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string CollapseSpaces(string s) =>
        string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
