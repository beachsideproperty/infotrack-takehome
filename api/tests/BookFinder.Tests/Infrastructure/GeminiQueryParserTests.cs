using BookFinder.Infrastructure.Gemini;
using Hyp = BookFinder.Infrastructure.Gemini.GeminiQueryParser.Hypothesis;

namespace BookFinder.Tests.Infrastructure;

public class GeminiQueryParserTests
{
    [Fact]
    public void Keeps_a_year_only_if_the_reader_typed_it()
    {
        var result = GeminiQueryParser.Sanitize("tolkien hobbit 1937", [new Hyp("The Hobbit", "J.R.R. Tolkien", [], 1937), new Hyp("The Hobbit", "Tolkien", [], 1951)]);

        Assert.Equal(1937, result[0].Year);
        Assert.Null(result[1].Year); // invented by the model
    }

    [Fact]
    public void A_number_that_is_the_title_is_not_a_year() =>
        Assert.Null(GeminiQueryParser.Sanitize("1984 by orwell", [new Hyp("1984", "George Orwell", [], 1984)])[0].Year);

    [Theory]
    [InlineData("1984 by orwell", "Nineteen Eighty-Four", 1984)] // what both models actually return (eval, Nov 25 2026)
    [InlineData("kubrick clarke 2001", "Two Thousand and One: A Space Odyssey", 2001)]
    public void A_year_the_title_spells_out_is_not_a_year(string query, string title, int year) =>
        Assert.Null(GeminiQueryParser.Sanitize(query, [new Hyp(title, null, [], year)])[0].Year);

    [Fact]
    public void A_year_next_to_an_unrelated_title_is_kept() =>
        Assert.Equal(1949, GeminiQueryParser.Sanitize("orwell 1949", [new Hyp("Nineteen Eighty-Four", "George Orwell", [], 1949)])[0].Year);

    [Theory]
    [InlineData(1984, "nineteen eighty four")]
    [InlineData(1900, "nineteen hundred")]
    [InlineData(1905, "nineteen oh five")]
    [InlineData(2001, "two thousand one")]
    [InlineData(2001, "two thousand and one")]
    [InlineData(2000, "two thousand")]
    public void Years_in_words(int year, string words) => Assert.Contains(words, GeminiQueryParser.YearInWords(year));

    [Fact]
    public void Trims_drops_empty_hypotheses_and_caps_at_three()
    {
        var raw = new[]
        {
            new Hyp("  Dune  ", " Frank Herbert ", ["  sci-fi "], null),
            new Hyp(" ", null, ["illustrated"], null), // only an edition word: nothing to search on
            new Hyp("Dune Messiah", null, null, null),
            new Hyp(null, "Brian Herbert", [], null),
            new Hyp("Children of Dune", null, [], null),
        };

        var result = GeminiQueryParser.Sanitize("dune herbert", raw);

        Assert.Equal(3, result.Count);
        Assert.Equal("Dune", result[0].Title);
        Assert.Equal("Frank Herbert", result[0].Author);
        Assert.Equal(["sci-fi"], result[0].Keywords);
        Assert.Equal("Dune Messiah", result[1].Title);
    }

    [Fact]
    public void A_vague_book_description_is_kept_as_keywords_only()
    {
        var result = GeminiQueryParser.Sanitize("blue cover book about a whale", [new Hyp(null, null, ["Whale", "book", "illustrated", "lighthouse"], null)]);

        var h = Assert.Single(result);
        Assert.True(h.IsKeywordOnly);
        Assert.Equal(["whale", "lighthouse"], h.Keywords);
    }

    [Fact]
    public void Only_edition_words_and_filler_is_not_a_book_query() =>
        Assert.Empty(GeminiQueryParser.Sanitize("illustrated book", [new Hyp(null, null, ["illustrated", "book"], null)]));

    [Fact]
    public void Series_flag_survives() =>
        Assert.True(GeminiQueryParser.Sanitize("harry potter", [new Hyp("Harry Potter", "J. K. Rowling", [], null, IsSeries: true)])[0].IsSeries);

    [Fact]
    public void Overlong_fields_are_truncated() =>
        Assert.Equal(200, GeminiQueryParser.Sanitize("x", [new Hyp(new string('a', 500), null, [], null)])[0].Title!.Length);
}
