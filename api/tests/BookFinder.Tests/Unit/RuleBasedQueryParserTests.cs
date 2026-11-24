using BookFinder.Application.Parsing;
using BookFinder.Domain;

namespace BookFinder.Tests.Unit;

public class RuleBasedQueryParserTests
{
    [Fact]
    public void Strips_edition_words_and_pulls_out_the_year()
    {
        var parsed = RuleBasedQueryParser.Parse("Tolkien Hobbit illustrated DELUXE 1937");

        var h = Assert.Single(parsed.Hypotheses);
        Assert.Equal(ParserKind.Fallback, parsed.Parser);
        Assert.Equal("tolkien hobbit", h.FreeText);
        Assert.Equal(1937, h.Year);
        Assert.Equal(["illustrated", "deluxe"], h.Keywords);
    }

    [Fact]
    public void Drops_filler_words() =>
        Assert.Equal("dune frank herbert", RuleBasedQueryParser.Parse("that book called dune by frank herbert").Hypotheses[0].FreeText);

    [Fact]
    public void Nothing_left_means_no_hypotheses() =>
        Assert.Empty(RuleBasedQueryParser.Parse("illustrated deluxe edition").Hypotheses);

    [Fact]
    public void Only_the_first_plausible_year_is_a_year()
    {
        var h = RuleBasedQueryParser.Parse("1984 orwell 1949").Hypotheses[0];

        Assert.Equal(1984, h.Year);
        Assert.Equal("orwell 1949", h.FreeText);
    }
}
