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

    [Fact]
    public void Trims_drops_empty_hypotheses_and_caps_at_three()
    {
        var raw = new[]
        {
            new Hyp("  Dune  ", " Frank Herbert ", ["  sci-fi "], null),
            new Hyp(" ", null, ["noise"], null),
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
    public void Series_flag_survives() =>
        Assert.True(GeminiQueryParser.Sanitize("harry potter", [new Hyp("Harry Potter", "J. K. Rowling", [], null, IsSeries: true)])[0].IsSeries);

    [Fact]
    public void Overlong_fields_are_truncated() =>
        Assert.Equal(200, GeminiQueryParser.Sanitize("x", [new Hyp(new string('a', 500), null, [], null)])[0].Title!.Length);
}
