using System.Text.RegularExpressions;
using BookFinder.Application.Text;
using BookFinder.Domain;

namespace BookFinder.Application.Parsing;

/// <summary>
/// No-LLM fallback. It can't tell a title from an author, so it strips noise (edition words, filler, a year)
/// and hands the rest to Open Library as free text; the classifier works out which words were the author.
/// </summary>
public static partial class RuleBasedQueryParser
{
    // Articles stay in: they can be part of a title ("the hobbit"), and Open Library's search handles them.
    private static readonly HashSet<string> Filler = ["book", "books", "novel", "by", "that", "about", "called", "titled", "written"];

    public static ParsedQuery Parse(string query)
    {
        var tokens = TextNormalizer.Tokens(query);
        int? year = null;
        var keywords = new List<string>();
        var kept = new List<string>();

        foreach (var token in tokens)
        {
            if (year is null && YearPattern().IsMatch(token)) year = int.Parse(token);
            else if (QueryWords.EditionWords.Contains(token)) keywords.Add(token);
            else if (!Filler.Contains(token)) kept.Add(token);
        }

        if (kept.Count == 0) return new ParsedQuery([], ParserKind.Fallback);

        var hypothesis = new QueryHypothesis(null, null, keywords, year, FreeText: string.Join(' ', kept));
        return new ParsedQuery([hypothesis], ParserKind.Fallback);
    }

    [GeneratedRegex("^(1[4-9]|20)[0-9]{2}$")]
    private static partial Regex YearPattern();
}
