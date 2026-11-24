using BookFinder.Application.Text;

namespace BookFinder.Tests.Unit;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Les Misérables", "les miserables")]
    [InlineData("J.R.R. Tolkien", "j r r tolkien")]
    [InlineData("  Harry   Potter!! ", "harry potter")]
    [InlineData("Harry Potter and the Philosopher's Stone", "harry potter and the philosophers stone")]
    [InlineData("Pride & Prejudice", "pride and prejudice")]
    [InlineData("Nineteen Eighty-Four", "nineteen eighty four")]
    [InlineData("Ærø Ünïcödé", "ærø unicode")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_strips_accents_punctuation_and_case(string? input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Normalize(input));

    [Theory]
    [InlineData("The Hobbit", "hobbit")]
    [InlineData("A Tale of Two Cities", "tale of two cities")]
    [InlineData("An Unquiet Mind", "unquiet mind")]
    [InlineData("The", "the")]
    [InlineData("Theory of Everything", "theory of everything")]
    public void NormalizeTitle_drops_one_leading_article(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.NormalizeTitle(input));
}
