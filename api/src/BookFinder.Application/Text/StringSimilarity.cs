namespace BookFinder.Application.Text;

public static class StringSimilarity
{
    /// <summary>1 − Levenshtein distance / longer length. 1.0 means identical.</summary>
    public static double LevenshteinRatio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        var longer = Math.Max(a.Length, b.Length);
        return 1.0 - (double)Levenshtein(a, b) / longer;
    }

    /// <summary>Sørensen–Dice over distinct tokens: 2|A∩B| / (|A|+|B|).</summary>
    public static double TokenDice(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        var setA = a.ToHashSet(StringComparer.Ordinal);
        var setB = b.ToHashSet(StringComparer.Ordinal);
        if (setA.Count + setB.Count == 0) return 1;
        var overlap = setA.Count(setB.Contains);
        return 2.0 * overlap / (setA.Count + setB.Count);
    }

    public static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
