using BookFinder.Application.Matching;
using BookFinder.Domain;

namespace BookFinder.Tests.Unit;

public class TierClassifierTests
{
    private static readonly QueryHypothesis Hobbit = H.Hyp("The Hobbit", "J.R.R. Tolkien", 1937);

    [Fact]
    public void A_exact_title_and_primary_author()
    {
        var ranked = TierClassifier.Classify(Books.Book("The Hobbit", ["J.R.R. Tolkien"], year: 1937), [Hobbit])!;

        Assert.Equal(MatchTier.A, ranked.Tier);
        Assert.Equal(AuthorRole.Lead, ranked.Evidence.AuthorRole);
        Assert.True(ranked.Evidence.YearMatched);
    }

    [Fact]
    public void B_exact_title_with_author_only_as_contributor()
    {
        var book = Books.Book("The Hobbit", ["Charles Dixon"], contributors: ["J.R.R. Tolkien"]);

        Assert.Equal(MatchTier.B, TierClassifier.Classify(book, [Hobbit])!.Tier);
    }

    [Fact]
    public void C_near_title_and_author()
    {
        var book = Books.Book("Harry Potter and the Philosopher's Stone", ["J. K. Rowling"]);

        var ranked = TierClassifier.Classify(book, [H.Hyp("hary poter stone", "Rowling")])!;

        Assert.Equal(MatchTier.C, ranked.Tier);
    }

    [Fact]
    public void D_author_work_only_when_primary_author()
    {
        Assert.Equal(MatchTier.D, TierClassifier.ClassifyAuthorWork(Books.Book("Oliver Twist", ["Charles Dickens"]), "Charles Dickens")!.Tier);
        Assert.Null(TierClassifier.ClassifyAuthorWork(Books.Book("Oliver! (musical)", ["Lionel Bart"], ["Charles Dickens"]), "Charles Dickens"));
    }

    [Theory]
    [InlineData("The Hobbit", "Someone Else")] // title only
    [InlineData("Poems from The hobbit", "J.R.R. Tolkien")] // author only
    public void E_when_only_title_or_only_author_matches(string title, string author) =>
        Assert.Equal(MatchTier.E, TierClassifier.Classify(Books.Book(title, [author]), [Hobbit])!.Tier);

    [Fact]
    public void Null_when_nothing_matches() =>
        Assert.Null(TierClassifier.Classify(Books.Book("Pizza Recipes", ["Abdul Riaz"]), [Hobbit]));

    [Fact]
    public void Collections_are_capped_at_E()
    {
        var boxSet = Books.Book("Harry Potter (series) 1-7", ["J. K. Rowling"]);

        var ranked = TierClassifier.Classify(boxSet, [H.Hyp("Harry Potter", "J. K. Rowling")])!;

        Assert.Equal(MatchTier.E, ranked.Tier);
        Assert.True(ranked.Evidence.Collection);
    }

    [Fact]
    public void Series_query_makes_each_book_a_near_match()
    {
        var book = Books.Book("Harry Potter and the Chamber of Secrets", ["J. K. Rowling"]);

        var ranked = TierClassifier.Classify(book, [H.Hyp("Harry Potter", "J. K. Rowling", series: true)])!;

        Assert.Equal(MatchTier.C, ranked.Tier);
        Assert.True(ranked.Evidence.SeriesMember);
    }

    [Fact]
    public void Series_title_by_someone_else_is_only_E()
    {
        // Starts with the series name, but the author check keeps it at the bottom.
        var cookbook = Books.Book("The Harry Potter Cookbook", ["Tom Grimm"]);

        Assert.Equal(MatchTier.E, TierClassifier.Classify(cookbook, [H.Hyp("Harry Potter", "J. K. Rowling", series: true)])!.Tier);
    }

    [Fact]
    public void Best_hypothesis_wins()
    {
        var book = Books.Book("Harry Potter and the Sorcerer's Stone", ["J. K. Rowling"]);
        var hypotheses = new[]
        {
            H.Hyp("Harry Potter and the Philosopher's Stone", "J. K. Rowling"),
            H.Hyp("Harry Potter and the Sorcerer's Stone", "J. K. Rowling"),
        };

        var ranked = TierClassifier.Classify(book, hypotheses)!;

        Assert.Equal(MatchTier.A, ranked.Tier);
        Assert.Equal(1, ranked.Evidence.HypothesisIndex);
    }

    [Fact]
    public void Fallback_free_text_finds_the_author_words_itself()
    {
        var book = Books.Book("The Hobbit", ["J.R.R. Tolkien"], year: 1937);
        var hypothesis = new QueryHypothesis(null, null, ["illustrated"], 1937, FreeText: "tolkien hobbit");

        var ranked = TierClassifier.Classify(book, [hypothesis])!;

        Assert.Equal(MatchTier.A, ranked.Tier);
        Assert.Equal("J.R.R. Tolkien", ranked.Evidence.MatchedAuthor?.Name);
    }

    private static RankedCandidate? FreeText(BookCandidate book, string text) =>
        TierClassifier.Classify(book, [new QueryHypothesis(null, null, [], null, FreeText: text)]);

    [Fact]
    public void Fallback_free_text_matches_part_of_a_title_and_a_first_name()
    {
        var ranked = FreeText(Books.Book("Adventures of Huckleberry Finn", ["Mark Twain"]), "mark huckleberry")!;

        Assert.Equal(MatchTier.C, ranked.Tier); // near title + author
        Assert.Equal(TitleMatch.Near, ranked.Evidence.Title);
        Assert.Equal("Mark Twain", ranked.Evidence.MatchedAuthor?.Name);
    }

    [Fact]
    public void Fallback_a_first_name_alone_never_matches()
    {
        // "mark" is Mark Twain's first name, but nothing else in the text matches this title.
        Assert.Null(FreeText(Books.Book("The Adventures of Tom Sawyer", ["Mark Twain"]), "mark huckleberry"));
        Assert.Null(FreeText(Books.Book("Roughing It", ["Mark Twain"]), "mark"));
    }

    [Fact]
    public void Fallback_free_text_series_words_match_each_book_as_near()
    {
        var ranked = FreeText(Books.Book("Harry Potter and the Chamber of Secrets", ["J. K. Rowling"]), "harry potter")!;

        Assert.Equal(TitleMatch.Near, ranked.Evidence.Title);
        Assert.Equal(MatchTier.E, ranked.Tier); // title only: no author in the text
    }

    [Theory]
    [InlineData("huckleberry", "Adventures of Huckleberry Finn", true)]
    [InlineData("hary poter", "Harry Potter and the Chamber of Secrets", true)] // typos
    [InlineData("pizza recipe", "Perfect pizza recipes", true)]
    [InlineData("huckleberry sawyer", "Adventures of Huckleberry Finn", false)] // every word must be there
    [InlineData("the of", "The Lord of the Rings", false)] // minor words alone don't count
    public void Has_all_words(string text, string title, bool expected) =>
        Assert.Equal(expected, TitleMatcher.HasAllWords(text, title));

    [Fact]
    public void Adaptation_listing_the_author_second_is_demoted_when_the_original_is_present()
    {
        // Open Library's graphic-novel Hobbit lists Dixon, Deming, then Tolkien, all as authors.
        var original = TierClassifier.Classify(Books.Book("The Hobbit", ["J.R.R. Tolkien"], year: 1937), [Hobbit])!;
        var graphicNovel = TierClassifier.Classify(Books.Book("The Hobbit", ["Charles Dixon", "Sean Deming", "J.R.R. Tolkien"]), [Hobbit])!;
        Assert.Equal(MatchTier.A, graphicNovel.Tier);

        var result = TierClassifier.DemoteDerivatives([original, graphicNovel]);

        Assert.Equal(MatchTier.A, result[0].Tier);
        Assert.Equal(MatchTier.B, result[1].Tier);
        Assert.True(result[1].Evidence.Derivative);
        Assert.Equal(AuthorRole.Contributor, result[1].Evidence.AuthorRole);
    }

    [Fact]
    public void Co_authors_stay_primary_when_no_other_version_exists()
    {
        var goodOmens = TierClassifier.Classify(
            Books.Book("Good Omens", ["Terry Pratchett", "Neil Gaiman"]), [H.Hyp("Good Omens", "Neil Gaiman")])!;

        var result = TierClassifier.DemoteDerivatives([goodOmens]);

        Assert.Equal(MatchTier.A, Assert.Single(result).Tier);
    }
}
