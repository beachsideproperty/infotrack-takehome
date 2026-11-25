namespace BookFinder.Application.Text;

/// <summary>
/// Numbers as titles spell them: "1984" is "Nineteen Eighty-Four", "5" in "Slaughterhouse 5" is "Five".
/// Output is normalized (lowercase, no hyphens) so it compares directly with <see cref="TextNormalizer.NormalizeTitle"/>.
/// </summary>
public static class NumberWords
{
    private static readonly string[] Ones =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
         "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];

    private static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// <summary>Largest number spelled out; bigger ones are left as digits.</summary>
    public const int Max = 9999;

    /// <summary>
    /// The usual way to say <paramref name="n"/> (0–9999): 1100–1999 the way years are read ("nineteen eighty four",
    /// "nineteen hundred", "nineteen oh five"); otherwise as a count ("four hundred fifty one", "two thousand one").
    /// </summary>
    public static string Spell(int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, Max);
        if (n is >= 1100 and <= 1999) return YearInWords(n).First();
        if (n < 100) return UnderHundred(n);
        if (n < 1000) return Join($"{Ones[n / 100]} hundred", n % 100);
        return Join($"{Ones[n / 1000]} thousand", n % 1000);
    }

    /// <summary>How a year can be spelled in a title: 1984 → "nineteen eighty four"; 2001 → "two thousand one", "two thousand and one".</summary>
    public static IEnumerable<string> YearInWords(int year)
    {
        var (century, rest) = (year / 100, year % 100);
        if (century is >= 11 and <= 19)
            yield return UnderHundred(century) + " " + (rest == 0 ? "hundred" : rest < 10 ? "oh " + Ones[rest] : UnderHundred(rest));
        if (year % 1000 < 100 && year / 1000 is >= 1 and <= 9)
        {
            var thousands = Ones[year / 1000] + " thousand";
            yield return rest == 0 ? thousands : $"{thousands} {UnderHundred(rest)}";
            if (rest > 0) yield return $"{thousands} and {UnderHundred(rest)}";
        }
    }

    /// <summary>Replaces each whole number in already-normalized text with its words: "catch 22" → "catch twenty two".</summary>
    public static string SpellAll(string normalized)
    {
        if (!normalized.Any(char.IsAsciiDigit)) return normalized;
        var words = normalized.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (IsSpellable(words[i], out var n)) words[i] = Spell(n);
        }
        return string.Join(' ', words);
    }

    /// <summary>True when the text has a number that <see cref="SpellAll"/> would change.</summary>
    public static bool HasSpellableNumber(string normalized) => normalized.Split(' ').Any(w => IsSpellable(w, out _));

    // Digits only, no leading zero ("007" stays), at most 9999.
    private static bool IsSpellable(string word, out int n)
    {
        n = 0;
        return word.Length is > 0 and <= 4 && word.All(char.IsAsciiDigit) && (word == "0" || word[0] != '0') && int.TryParse(word, out n);
    }

    private static string UnderHundred(int n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");

    private static string Join(string head, int rest) => rest == 0 ? head : $"{head} {Spell(rest)}";
}
