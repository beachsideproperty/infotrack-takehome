using BookFinder.Application.Matching;
using BookFinder.Application.Text;
using BookFinder.Domain;

namespace BookFinder.Tests.Unit;

public class NumberWordsTests
{
    [Theory]
    [InlineData(0, "zero")]
    [InlineData(5, "five")]
    [InlineData(22, "twenty two")]
    [InlineData(451, "four hundred fifty one")]
    [InlineData(1984, "nineteen eighty four")]
    [InlineData(1900, "nineteen hundred")]
    [InlineData(1905, "nineteen oh five")]
    [InlineData(2001, "two thousand one")]
    [InlineData(1000, "one thousand")]
    public void Spells_numbers_the_way_titles_do(int n, string expected) => Assert.Equal(expected, NumberWords.Spell(n));

    [Theory]
    [InlineData(1984, "nineteen eighty four")]
    [InlineData(1900, "nineteen hundred")]
    [InlineData(1905, "nineteen oh five")]
    [InlineData(2001, "two thousand one")]
    [InlineData(2001, "two thousand and one")]
    [InlineData(2000, "two thousand")]
    public void Years_in_words(int year, string words) => Assert.Contains(words, NumberWords.YearInWords(year));

    [Theory]
    [InlineData("catch 22", "catch twenty two")]
    [InlineData("1984", "nineteen eighty four")]
    [InlineData("agent 007", "agent 007")]          // leading zero: a code name, not a number
    [InlineData("20000 leagues", "20000 leagues")]  // too big to be worth spelling
    [InlineData("3d printing", "3d printing")]      // not a whole word of digits
    [InlineData("no numbers", "no numbers")]
    public void Spells_every_whole_number_in_normalized_text(string text, string expected) =>
        Assert.Equal(expected, NumberWords.SpellAll(text));

    [Theory]
    [InlineData("1984", "Nineteen Eighty-Four")]
    [InlineData("Slaughterhouse 5", "Slaughterhouse-Five")]
    [InlineData("Nineteen Eighty-Four", "1984")]
    public void Digits_and_words_are_the_same_title(string query, string title) =>
        Assert.Equal(TitleMatch.Exact, TitleMatcher.Compare(query, title));

    [Fact]
    public void Duplicate_records_written_both_ways_share_a_base_title() =>
        Assert.Equal(TitleVariants.Base("Nineteen Eighty-Four"), TitleVariants.Base("1984"));
}
