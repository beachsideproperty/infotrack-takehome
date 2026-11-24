using BookFinder.Application.Explanations;
using BookFinder.Application.Matching;
using BookFinder.Domain;

namespace BookFinder.Tests.Unit;

public class TemplateExplanationsTests
{
    private static readonly QueryHypothesis Hobbit = H.Hyp("The Hobbit", "J.R.R. Tolkien", 1937);

    [Fact]
    public void Tier_A_cites_primary_author_contributor_and_year()
    {
        var ranked = TierClassifier.Classify(Books.Book("The Hobbit", ["J.R.R. Tolkien"], ["Charles Dixon"], year: 1937), [Hobbit])!;

        Assert.Equal(
            "Exact title match; J.R.R. Tolkien is the primary author on the work record; Charles Dixon listed as a contributor; first published in 1937, the year you gave.",
            TemplateExplanations.For(ranked, Hobbit, 0));
    }

    [Fact]
    public void Derivative_names_who_is_listed_first()
    {
        var original = TierClassifier.Classify(Books.Book("The Hobbit", ["J.R.R. Tolkien"]), [Hobbit])!;
        var graphicNovel = TierClassifier.Classify(Books.Book("The Hobbit", ["Charles Dixon", "J.R.R. Tolkien"]), [Hobbit])!;
        var demoted = TierClassifier.DemoteDerivatives([original, graphicNovel])[1];

        var text = TemplateExplanations.For(demoted, Hobbit, 1);

        Assert.Contains("Charles Dixon is listed first", text);
        Assert.Contains("J.R.R. Tolkien is credited after them", text);
    }

    [Theory]
    [InlineData(0, "Most-read Charles Dickens work")]
    [InlineData(1, "2nd most-read Charles Dickens work")]
    [InlineData(2, "3rd most-read")]
    [InlineData(10, "11th most-read")]
    public void Tier_D_gives_reading_log_position(int position, string expected)
    {
        var ranked = TierClassifier.ClassifyAuthorWork(Books.Book("Oliver Twist", ["Charles Dickens"]), "Charles Dickens")!;

        Assert.Contains(expected, TemplateExplanations.For(ranked, null, position));
    }

    [Fact]
    public void Weak_match_says_what_did_not_match()
    {
        var ranked = TierClassifier.Classify(Books.Book("The Hobbit", ["Someone Else"]), [Hobbit])!;

        Assert.Equal(
            "The title matches what you typed, but the author on record is Someone Else, not J.R.R. Tolkien.",
            TemplateExplanations.For(ranked, Hobbit, 0));
    }
}
