using BookFinder.Application.Search;
using BookFinder.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookFinder.Tests.Golden;

/// <summary>
/// The spec's golden queries, end to end through the real <see cref="SearchService"/> and Open Library client,
/// replaying recorded Open Library JSON (no network). Gemini is replaced by the hypotheses it actually returned
/// on Nov 24, 2026, so these tests pin down what code decides given a reading of the query.
/// Refresh fixtures: <c>RECORD_FIXTURES=1 dotnet test --filter Category=Golden</c>.
/// </summary>
[Trait("Category", "Golden")]
public class GoldenQueryTests
{
    private static (SearchService Service, FixtureHandler Http) Create(FakeLlmParser parser)
    {
        var http = new FixtureHandler();
        var service = new SearchService(parser, Catalogs.OpenLibrary(http), new UnavailableExplainer(), new MemoryCache(new MemoryCacheOptions()), NullLogger<SearchService>.Instance);
        return (service, http);
    }

    private static Task<SearchOutcome> Search(string query, params QueryHypothesis[] llmReading) =>
        Create(FakeLlmParser.Returning(llmReading)).Service.SearchAsync(query, default);

    private static void AssertTop(SearchOutcome outcome, string title, string author, MatchTier tier)
    {
        var top = outcome.Results[0].Candidate;
        Assert.Equal(title, top.Book.Title);
        Assert.Equal(author, top.Book.LeadAuthor?.Name);
        Assert.Equal(tier, top.Tier);
        Assert.All(outcome.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.Explanation)));
    }

    [Fact]
    public async Task Dickens_is_author_only_most_read_first()
    {
        var outcome = await Search("dickens", H.Hyp(null, "Charles Dickens"));

        Assert.Equal(SearchMode.AuthorOnly, outcome.Mode);
        Assert.False(outcome.HasBestMatch);
        AssertTop(outcome, "A Tale of Two Cities", "Charles Dickens", MatchTier.D);
        Assert.All(outcome.Results, r => Assert.Equal(MatchTier.D, r.Candidate.Tier));
        Assert.Equal(5, outcome.Results.Count);
        Assert.Equal(outcome.Results.Count, outcome.Results.Select(r => r.Candidate.Book.Title).Distinct().Count());
        Assert.Equal(new AuthorRef("OL24638A", "Charles Dickens"), outcome.Author);
    }

    [Fact]
    public async Task Tale_two_cities()
    {
        var outcome = await Search("tale two cities", H.Hyp("A Tale of Two Cities", "Charles Dickens"));

        AssertTop(outcome, "A Tale of Two Cities", "Charles Dickens", MatchTier.A);
        Assert.True(outcome.HasBestMatch);
    }

    [Fact]
    public async Task Tolkien_hobbit_illustrated_deluxe_1937_ranks_the_original_over_the_adaptation()
    {
        var outcome = await Search("tolkien hobbit illustrated deluxe 1937",
            new QueryHypothesis("The Hobbit", "J.R.R. Tolkien", ["illustrated", "deluxe"], 1937));

        AssertTop(outcome, "The Hobbit", "J.R.R. Tolkien", MatchTier.A);
        Assert.True(outcome.Results[0].Candidate.Evidence.YearMatched);
        Assert.True(outcome.HasBestMatch);

        // Charles Dixon's graphic novel: same title, Tolkien credited after Dixon → contributor, tier B.
        var dixon = outcome.Results.Single(r => r.Candidate.Book.LeadAuthor?.Name == "Charles Dixon");
        Assert.Equal(MatchTier.B, dixon.Candidate.Tier);
        Assert.True(dixon.Candidate.Evidence.Derivative);
        Assert.Contains("Charles Dixon is listed first", dixon.Explanation);
    }

    [Fact]
    public async Task Mark_huckleberry()
    {
        var outcome = await Search("mark huckleberry", H.Hyp("Adventures of Huckleberry Finn", "Mark Twain"));

        AssertTop(outcome, "Adventures of Huckleberry Finn", "Mark Twain", MatchTier.A);
        // Open Library has several work records for this book; only the canonical one survives.
        Assert.Single(outcome.Results, r => r.Candidate.Book.Title == "Adventures of Huckleberry Finn" && r.Candidate.Book.LeadAuthor?.Name == "Mark Twain");
    }

    [Fact]
    public async Task Twilight_meyer()
    {
        var outcome = await Search("twilight meyer", H.Hyp("Twilight", "Stephenie Meyer"));

        AssertTop(outcome, "Twilight", "Stephenie Meyer", MatchTier.A);
        Assert.True(outcome.HasBestMatch);
    }

    [Fact]
    public async Task Austen_bennet()
    {
        var outcome = await Search("austen bennet", H.Hyp("Pride and Prejudice", "Jane Austen"));

        AssertTop(outcome, "Pride and Prejudice", "Jane Austen", MatchTier.A);
    }

    [Fact]
    public async Task Nineteen_eighty_four_by_orwell()
    {
        var outcome = await Search("1984 by orwell", H.Hyp("Nineteen Eighty-Four", "George Orwell"));

        AssertTop(outcome, "Nineteen Eighty-Four", "George Orwell", MatchTier.A);
        Assert.Equal(1949, outcome.Results[0].Candidate.Book.FirstPublishYear);
    }

    [Fact]
    public async Task Nineteen_eighty_four_when_gemini_reads_the_title_as_digits()
    {
        // Gemini sometimes returns the title "1984". Open Library's "1984" title search doesn't include Orwell's
        // main work, so the spelled-out title is searched too, and the sparse "1984" record collapses into it.
        var outcome = await Search("1984 by orwell", H.Hyp("1984", "George Orwell"));

        AssertTop(outcome, "Nineteen Eighty-Four", "George Orwell", MatchTier.A);
        Assert.Equal("OL1168083W", outcome.Results[0].Candidate.Book.WorkKey.Split('/')[^1]);
        Assert.True(outcome.HasBestMatch);
    }

    [Fact]
    public async Task Hary_poter_stone()
    {
        var outcome = await Search("hary poter stone",
            H.Hyp("Harry Potter and the Philosopher's Stone", "J. K. Rowling"),
            H.Hyp("Harry Potter and the Sorcerer's Stone", "J. K. Rowling"));

        AssertTop(outcome, "Harry Potter and the Philosopher's Stone", "J. K. Rowling", MatchTier.A);
    }

    [Fact]
    public async Task Harry_potter_is_a_series_so_a_ranked_list_with_no_winner()
    {
        var outcome = await Search("harry potter", H.Hyp("Harry Potter", "J. K. Rowling", series: true));

        Assert.Equal(SearchMode.Match, outcome.Mode);
        Assert.False(outcome.HasBestMatch);
        Assert.True(outcome.Results.Count >= 3);
        Assert.All(outcome.Results.Take(3), r =>
        {
            Assert.StartsWith("Harry Potter and the", r.Candidate.Book.Title);
            Assert.True(r.Candidate.Evidence.SeriesMember);
        });
    }

    [Fact]
    public async Task Les_miserables_with_diacritics()
    {
        var outcome = await Search("les miserables", H.Hyp("Les Misérables", "Victor Hugo"));

        AssertTop(outcome, "Les Misérables", "Victor Hugo", MatchTier.A);
    }

    [Fact]
    public async Task Pizza_recipe_is_not_a_book_and_never_reaches_open_library()
    {
        var (service, http) = Create(FakeLlmParser.Returning());

        var outcome = await service.SearchAsync("pizza recipe", default);

        Assert.Equal(SearchMode.NoMatch, outcome.Mode);
        Assert.Empty(outcome.Results);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Whale_lighthouse_is_vague_so_closest_guesses_without_a_match()
    {
        var outcome = await Search("blue cover book about a whale and a lighthouse",
            new QueryHypothesis(null, null, ["whale", "lighthouse"], null));

        Assert.Equal(SearchMode.NoMatch, outcome.Mode);
        Assert.False(outcome.HasBestMatch);
        Assert.InRange(outcome.Results.Count, 1, 5);
        Assert.All(outcome.Results, r =>
        {
            Assert.Equal(MatchTier.E, r.Candidate.Tier);
            Assert.True(r.Candidate.Evidence.KeywordOnly);
            Assert.False(string.IsNullOrWhiteSpace(r.Explanation));
        });
    }

    // ---- Same queries with Gemini down: the rule-based fallback still gets there.

    [Fact]
    public async Task Fallback_tolkien_hobbit()
    {
        var outcome = await Create(FakeLlmParser.Unavailable).Service.SearchAsync("tolkien hobbit illustrated deluxe 1937", default);

        Assert.Equal(ParserKind.Fallback, outcome.Parsed.Parser);
        AssertTop(outcome, "The Hobbit", "J.R.R. Tolkien", MatchTier.A);
    }

    [Fact]
    public async Task Fallback_dickens_is_still_author_only()
    {
        var outcome = await Create(FakeLlmParser.Unavailable).Service.SearchAsync("dickens", default);

        Assert.Equal(SearchMode.AuthorOnly, outcome.Mode);
        AssertTop(outcome, "A Tale of Two Cities", "Charles Dickens", MatchTier.D);
        Assert.Equal("OL24638A", outcome.Author?.Key);
    }

    private static Task<SearchOutcome> Fallback(string query) => Create(FakeLlmParser.Unavailable).Service.SearchAsync(query, default);

    [Fact]
    public async Task Fallback_mark_huckleberry_matches_a_first_name_and_part_of_the_title()
    {
        var outcome = await Fallback("mark huckleberry");

        AssertTop(outcome, "Adventures of Huckleberry Finn", "Mark Twain", MatchTier.C);
        Assert.True(outcome.HasBestMatch);
    }

    [Fact]
    public async Task Fallback_twilight_meyer()
    {
        var outcome = await Fallback("twilight meyer");

        AssertTop(outcome, "Twilight", "Stephenie Meyer", MatchTier.A);
        Assert.True(outcome.HasBestMatch);
    }

    [Fact]
    public async Task Fallback_les_miserables_handles_the_accent()
    {
        var outcome = await Fallback("les miserables");

        Assert.Equal("Victor Hugo", outcome.Results[0].Candidate.Book.LeadAuthor?.Name);
        Assert.Equal("Les Misérables", outcome.Results[0].Candidate.Book.Title);
    }

    [Fact]
    public async Task Fallback_austen_bennet_finds_pride_and_prejudice_through_open_library_full_text()
    {
        // No AI to turn the character into a title, but Open Library's full-text search knows Bennet.
        var outcome = await Fallback("austen bennet");

        Assert.Equal("Pride and Prejudice", outcome.Results[0].Candidate.Book.Title);
        Assert.False(outcome.HasBestMatch); // only the author matched; honest about it
    }

    [Fact]
    public async Task Fallback_harry_potter_lists_the_books_with_no_winner()
    {
        var outcome = await Fallback("harry potter");

        Assert.False(outcome.HasBestMatch);
        Assert.Contains(outcome.Results, r => r.Candidate.Book.Title == "Harry Potter and the Philosopher's Stone");
        Assert.True(outcome.Results.Count >= 3);
    }

    // Known limits without the AI, pinned so a change in behaviour is noticed (README: failures and unclear results).

    [Fact]
    public async Task Fallback_limit_1984_is_read_as_a_year_so_orwell_becomes_author_only()
    {
        var outcome = await Fallback("1984 by orwell");

        Assert.Equal(SearchMode.AuthorOnly, outcome.Mode);
        Assert.Equal("George Orwell", outcome.Author?.Name);
    }

    [Fact]
    public async Task Fallback_limit_tale_two_cities_puts_a_literal_title_match_first()
    {
        // "A tale two cities" (H. R. Jamison) is an exact title match for the words typed, so the hierarchy puts it
        // first; Dickens is next. Gemini reads the real title, so this only happens when it's down.
        var outcome = await Fallback("tale two cities");

        Assert.Equal("H. R. Jamison", outcome.Results[0].Candidate.Book.LeadAuthor?.Name);
        Assert.Equal("Charles Dickens", outcome.Results[1].Candidate.Book.LeadAuthor?.Name);
        Assert.False(outcome.HasBestMatch);
    }

    [Fact]
    public async Task Fallback_limit_pizza_recipe_shows_pizza_books_with_no_winner()
    {
        // Rules can't tell "not a book" from a topic; they show what Open Library has, never as a best match.
        var outcome = await Fallback("pizza recipe");

        Assert.False(outcome.HasBestMatch);
        Assert.All(outcome.Results, r => Assert.Equal(MatchTier.E, r.Candidate.Tier));
    }

    [Fact]
    public async Task Fallback_limit_a_described_book_finds_nothing_without_the_ai()
    {
        // Gemini turns this into keyword guesses; the rules pass all the words to Open Library, which matches nothing.
        var outcome = await Fallback("blue cover book about a whale and a lighthouse");

        Assert.Equal(SearchMode.NoMatch, outcome.Mode);
        Assert.Empty(outcome.Results);
    }

    [Fact]
    public async Task Fallback_typos_still_find_the_book()
    {
        var outcome = await Create(FakeLlmParser.Unavailable).Service.SearchAsync("hary poter stone", default);

        Assert.Equal("Harry Potter and the Philosopher's Stone", outcome.Results[0].Candidate.Book.Title);
    }
}
