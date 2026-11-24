using BookFinder.Application.Ports;
using BookFinder.Application.Search;
using BookFinder.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookFinder.Tests.Unit;

/// <summary>Caching and quota-saving behaviour of <see cref="SearchService"/>, with counting fakes.</summary>
public class SearchServiceCacheTests
{
    private sealed class CountingParser(Func<IReadOnlyList<QueryHypothesis>> answer) : ILlmQueryParser
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<QueryHypothesis>> ParseAsync(string query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    private sealed class CountingExplainer(bool works = true) : IExplanationWriter
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, string>> WriteAsync(string query, IReadOnlyList<RankedCandidate> candidates, CancellationToken cancellationToken)
        {
            Calls++;
            if (!works) throw new LlmUnavailableException("test");
            return Task.FromResult<IReadOnlyDictionary<string, string>>(candidates.ToDictionary(c => c.Book.WorkKey, _ => "LLM sentence."));
        }
    }

    private static readonly QueryHypothesis Hobbit = new("The Hobbit", "J.R.R. Tolkien", [], 1937);

    private static SearchService Service(ILlmQueryParser parser, IExplanationWriter explainer, IMemoryCache cache) =>
        new(parser, Catalogs.OpenLibrary(new FixtureHandler()), explainer, cache, NullLogger<SearchService>.Instance);

    [Fact]
    public async Task Same_query_twice_calls_gemini_once()
    {
        var parser = new CountingParser(() => [Hobbit]);
        var explainer = new CountingExplainer();
        var service = Service(parser, explainer, new MemoryCache(new MemoryCacheOptions()));

        var first = await service.SearchAsync("tolkien hobbit illustrated deluxe 1937", default);
        var second = await service.SearchAsync("  Tolkien Hobbit, illustrated deluxe 1937! ", default);

        Assert.Equal((1, 1), (parser.Calls, explainer.Calls));
        Assert.Equal(first.Results.Select(r => r.Candidate.Book.WorkKey), second.Results.Select(r => r.Candidate.Book.WorkKey));
        Assert.Equal("  Tolkien Hobbit, illustrated deluxe 1937! ", second.Query); // echoes what this caller typed
    }

    [Fact]
    public async Task Degraded_results_expire_quickly_but_the_parse_is_kept()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var cache = new MemoryCache(new MemoryCacheOptions { Clock = new ClockAdapter(clock) });
        var parser = new CountingParser(() => [Hobbit]);
        var explainer = new CountingExplainer(works: false);
        var service = Service(parser, explainer, cache);

        var first = await service.SearchAsync("tolkien hobbit illustrated deluxe 1937", default);
        Assert.False(first.ExplanationsFromLlm);

        clock.Advance(SearchService.DegradedResultTtl + TimeSpan.FromSeconds(1));
        await service.SearchAsync("tolkien hobbit illustrated deluxe 1937", default);

        Assert.Equal(1, parser.Calls);    // Gemini's reading reused
        Assert.Equal(2, explainer.Calls); // explanations retried
    }

    [Fact]
    public async Task Explanations_are_skipped_when_parsing_fell_back()
    {
        var explainer = new CountingExplainer();
        var service = Service(FakeLlmParser.Unavailable, explainer, new MemoryCache(new MemoryCacheOptions()));

        var outcome = await service.SearchAsync("tolkien hobbit illustrated deluxe 1937", default);

        Assert.Equal(ParserKind.Fallback, outcome.Parsed.Parser);
        Assert.NotEmpty(outcome.Results);
        Assert.Equal(0, explainer.Calls);
    }

#pragma warning disable CS0618 // ISystemClock is the MemoryCache clock hook
    private sealed class ClockAdapter(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
#pragma warning restore CS0618
}
