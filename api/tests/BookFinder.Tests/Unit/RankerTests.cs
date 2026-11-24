using BookFinder.Application.Matching;
using BookFinder.Domain;

namespace BookFinder.Tests.Unit;

public class RankerTests
{
    private static RankedCandidate Classified(BookCandidate book, QueryHypothesis hypothesis) =>
        TierClassifier.Classify(book, [hypothesis])!;

    [Fact]
    public void Orders_by_tier_then_year_then_author_position_then_popularity()
    {
        var hypothesis = H.Hyp("The Hobbit", "Tolkien", 1937);
        var popularRetelling = Classified(Books.Book("The Hobbit", ["J.R.R. Tolkien"], year: 2001, readingLog: 9000, key: "/works/OL2W"), hypothesis);
        var original = Classified(Books.Book("The Hobbit", ["J.R.R. Tolkien"], year: 1937, readingLog: 100, key: "/works/OL1W"), hypothesis);
        var coCredited = Classified(Books.Book("The Hobbit", ["Charles Dixon", "J.R.R. Tolkien"], year: 1937, key: "/works/OL3W"), hypothesis);
        var weak = Classified(Books.Book("Poems from The hobbit", ["J.R.R. Tolkien"], readingLog: 99999, key: "/works/OL4W"), hypothesis);

        var ranked = Ranker.Rank([weak, popularRetelling, coCredited, original]);

        Assert.Equal(["/works/OL1W", "/works/OL3W", "/works/OL2W", "/works/OL4W"], ranked.Select(r => r.Book.WorkKey));
    }

    [Fact]
    public void Same_input_in_any_order_gives_the_same_ranking()
    {
        var hypothesis = H.Hyp("Twilight", "Stephenie Meyer");
        var candidates = Enumerable.Range(1, 8)
            .Select(i => Classified(Books.Book(i % 2 == 0 ? "Twilight" : "Twilight Saga", ["Stephenie Meyer"], readingLog: i % 3, key: $"/works/OL{i}W"), hypothesis))
            .ToList();
        var expected = Ranker.Rank(candidates, 8).Select(r => r.Book.WorkKey).ToList();

        for (var seed = 0; seed < 20; seed++)
        {
            var shuffled = candidates.OrderBy(_ => new Random(seed).Next()).ToList();
            Assert.Equal(expected, Ranker.Rank(shuffled, 8).Select(r => r.Book.WorkKey));
        }
    }

    [Fact]
    public void Returns_at_most_five()
    {
        var hypothesis = H.Hyp("Dune", "Frank Herbert");
        var many = Enumerable.Range(1, 9).Select(i => Classified(Books.Book("Dune", ["Frank Herbert"], key: $"/works/OL{i}W"), hypothesis));

        Assert.Equal(5, Ranker.Rank(many).Count);
    }

    [Fact]
    public void Clear_winner_needs_a_stronger_match_or_far_more_readers()
    {
        var hypothesis = H.Hyp("Twilight", "Stephenie Meyer");
        var twilight = Classified(Books.Book("Twilight", ["Stephenie Meyer"], readingLog: 5000), hypothesis);
        var saga = Classified(Books.Book("The Twilight Saga", ["Stephenie Meyer"]), hypothesis);
        var duplicate = Classified(Books.Book("Twilight", ["Stephenie Meyer"], readingLog: 4000), hypothesis);
        var minor = Classified(Books.Book("Twilight", ["Stephenie Meyer"], readingLog: 10), hypothesis);

        Assert.True(Ranker.HasClearWinner(Ranker.Rank([twilight, saga])));      // A beats E
        Assert.False(Ranker.HasClearWinner(Ranker.Rank([twilight, duplicate]))); // same strength, similar readers
        Assert.True(Ranker.HasClearWinner(Ranker.Rank([twilight, minor])));      // same strength, 500× the readers
        Assert.False(Ranker.HasClearWinner(Ranker.Rank([saga])));                // E is never a hero
        Assert.False(Ranker.HasClearWinner([]));
    }
}
