using BookFinder.Application.Matching;
using BookFinder.Application.Text;
using BookFinder.Domain;

namespace BookFinder.Tests.Unit;

public class TitleMatcherTests
{
    [Theory]
    [InlineData("The Hobbit", "The Hobbit")]
    [InlineData("hobbit", "The Hobbit, or There and Back Again")]
    [InlineData("the hobbit", "The Hobbit: Illustrated Edition")]
    [InlineData("Les Miserables", "Les Misérables")]
    [InlineData("Adventures of Huckleberry Finn", "The Adventures of Huckleberry Finn")]
    [InlineData("Frankenstein", "Frankenstein; or, The Modern Prometheus")]
    public void Exact_after_normalizing_and_title_variants(string query, string title) =>
        Assert.Equal(TitleMatch.Exact, TitleMatcher.Compare(query, title));

    [Theory]
    [InlineData("Harry Poter and the Philosophers Stone", "Harry Potter and the Philosopher's Stone")]
    [InlineData("hary poter stone", "Harry Potter and the Philosopher's Stone")]
    [InlineData("two cities tale", "A Tale of Two Cities")]
    [InlineData("A Tale of Two Cities", "A Tale of Two Cities [adaptation]")]
    public void Near_for_typos_reordering_and_annotations(string query, string title) =>
        Assert.Equal(TitleMatch.Near, TitleMatcher.Compare(query, title));

    [Theory]
    [InlineData("harry potter", "Harry Potter and the Philosopher's Stone")]
    [InlineData("hobbit", "Poems from The hobbit")]
    [InlineData("Twilight", "New Moon")]
    [InlineData("", "Anything")]
    public void None_when_the_query_covers_too_little(string query, string title) =>
        Assert.Equal(TitleMatch.None, TitleMatcher.Compare(query, title));

    [Theory]
    [InlineData("Harry Potter", "Harry Potter and the Chamber of Secrets", TitleMatch.Near)]
    [InlineData("Harry Potter", "Cooking with Harry Potter", TitleMatch.None)]
    [InlineData("Discworld", "Discworld: Guards! Guards!", TitleMatch.Near)]
    public void Series_matches_titles_that_start_with_the_series_name(string series, string title, TitleMatch expected) =>
        Assert.Equal(expected, TitleMatcher.CompareSeries(series, title));

    [Theory]
    [InlineData("Nineteen Eighty-Four (1984)", "nineteen eighty four")]
    [InlineData("Harry Potter and the Philosopher's Stone - Ukrainian Edition", "harry potter and the philosophers stone")]
    [InlineData("Dune: Deluxe Edition", "dune")]
    [InlineData("The Hobbit", "hobbit")]
    public void Base_title_drops_subtitles_and_edition_notes(string title, string expected) =>
        Assert.Equal(expected, TitleVariants.Base(title));
}
