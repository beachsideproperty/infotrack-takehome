using BookFinder.Application.Explanations;
using BookFinder.Application.Matching;
using BookFinder.Application.Parsing;
using BookFinder.Application.Ports;
using BookFinder.Application.Text;
using BookFinder.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BookFinder.Application.Search;

/// <summary>
/// query → hypotheses (Gemini, or rules) → Open Library search per hypothesis → dedupe → work records for
/// the top candidates → tier + rank → explanations (Gemini, or templates).
/// The LLM reads and explains; this class and <see cref="Ranker"/> decide.
/// Results are cached by normalized query so repeat searches never spend Gemini quota.
/// </summary>
public sealed class SearchService(
    ILlmQueryParser llmParser,
    IBookCatalog catalog,
    IExplanationWriter explanationWriter,
    IMemoryCache cache,
    ILogger<SearchService> logger)
{
    /// <summary>How long a result made entirely with Gemini is reused.</summary>
    public static readonly TimeSpan FullResultTtl = TimeSpan.FromHours(6);

    /// <summary>A result that needed the rule-based parser or templates is reused only briefly, so a better one
    /// replaces it once Gemini is answering again.</summary>
    public static readonly TimeSpan DegradedResultTtl = TimeSpan.FromMinutes(2);

    public const int MaxQueryLength = 300;
    public const int MaxHypotheses = 3;

    /// <summary>Work records are fetched only for this many candidates (Open Library budget).</summary>
    public const int WorkRecordsToFetch = 5;

    private const int SearchLimit = 10;
    private const int MaxExplanationLength = 300;

    public async Task<SearchOutcome> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var key = "search:" + TextNormalizer.Normalize(query);
        if (cache.TryGetValue(key, out SearchOutcome? cached) && cached is not null)
        {
            logger.LogInformation("Search cache hit for {Query}", query);
            return cached with { Query = query };
        }

        var outcome = await SearchUncachedAsync(query, cancellationToken);
        var degraded = outcome.Parsed.Parser == ParserKind.Fallback || (outcome.Results.Count > 0 && !outcome.ExplanationsFromLlm);
        cache.Set(key, outcome, degraded ? DegradedResultTtl : FullResultTtl);
        return outcome;
    }

    private async Task<SearchOutcome> SearchUncachedAsync(string query, CancellationToken cancellationToken)
    {
        var parsed = await ParseAsync(query, cancellationToken);
        var hypotheses = parsed.Hypotheses.Where(h => h.IsSearchable).Take(MaxHypotheses).ToList();
        parsed = parsed with { Hypotheses = hypotheses };

        // "pizza recipe": nothing that names a book or a person. Don't go fishing in Open Library.
        if (hypotheses.Count == 0) return NoMatch(query, parsed);

        IReadOnlyList<RankedCandidate> ranked;
        var mode = SearchMode.Match;
        AuthorRef? author = null;

        if (hypotheses.All(h => h.IsKeywordOnly))
        {
            // "blue cover book about a whale and a lighthouse": a book, but no title or author to go on.
            // Show Open Library's closest keyword matches as guesses, never as a match.
            var guesses = await KeywordGuessesAsync(hypotheses[0], cancellationToken);
            if (guesses.Count == 0) return NoMatch(query, parsed);

            var (guessResults, guessesFromLlm, guessFailure) = await ExplainAsync(query, guesses, hypotheses, parsed.Parser == ParserKind.Llm, cancellationToken);
            return NoMatch(query, parsed) with { Results = guessResults, ExplanationsFromLlm = guessesFromLlm, ExplanationFailure = guessFailure };
        }

        // Keyword-only readings add nothing once another reading names a title or author.
        hypotheses = hypotheses.Where(h => !h.IsKeywordOnly).ToList();
        parsed = parsed with { Hypotheses = hypotheses };

        if (hypotheses[0].IsAuthorOnly)
        {
            (ranked, author) = await AuthorWorksAsync(hypotheses[0].Author!, cancellationToken);
            mode = SearchMode.AuthorOnly;
        }
        else
        {
            var docs = await SearchHypothesesAsync(hypotheses, cancellationToken);
            if (hypotheses[0].IsFreeText && FreeTextAuthor(hypotheses[0], docs) is { } authorName)
            {
                // Fallback parser on "dickens": every word is the top result's author's name.
                (ranked, author) = await AuthorWorksAsync(authorName, cancellationToken);
                mode = SearchMode.AuthorOnly;
            }
            else
            {
                ranked = await RankAsync(docs, hypotheses, cancellationToken);
            }
        }

        if (ranked.Count == 0) return NoMatch(query, parsed);

        var (results, fromLlm, explanationFailure) = await ExplainAsync(query, ranked, hypotheses, parsed.Parser == ParserKind.Llm, cancellationToken);
        // A series ("harry potter") has no single answer: always a ranked list.
        var hasBestMatch = mode == SearchMode.Match && !hypotheses[0].IsSeries && Ranker.HasClearWinner(ranked);
        return new SearchOutcome(query, parsed, mode, null, results, hasBestMatch, fromLlm, author, explanationFailure);
    }

    /// <summary>Gemini's reading, cached separately so a degraded result can be redone without re-parsing.</summary>
    private async Task<ParsedQuery> ParseAsync(string query, CancellationToken cancellationToken)
    {
        var key = "parse:" + TextNormalizer.Normalize(query);
        if (cache.TryGetValue(key, out ParsedQuery? cached) && cached is not null) return cached;

        try
        {
            var hypotheses = await llmParser.ParseAsync(query, cancellationToken);
            var parsed = new ParsedQuery(hypotheses, ParserKind.Llm);
            cache.Set(key, parsed, FullResultTtl);
            return parsed;
        }
        catch (LlmUnavailableException ex)
        {
            logger.LogWarning("LLM parser unavailable ({LlmFailure}), using rule-based fallback: {Reason}", ex.Reason, ex.Message);
            return RuleBasedQueryParser.Parse(query) with { LlmFailure = ex.Reason };
        }
    }

    private SearchOutcome NoMatch(string query, ParsedQuery parsed)
    {
        // "Did you mean": Gemini's runner-up reading, when it names a specific title.
        var didYouMean = parsed.Parser == ParserKind.Llm && parsed.Hypotheses.Count > 1 && parsed.Hypotheses[1].HasTitle
            ? parsed.Hypotheses[1]
            : null;
        return new SearchOutcome(query, parsed, SearchMode.NoMatch, didYouMean, [], false, false);
    }

    /// <summary>One <c>search.json</c> per hypothesis, in parallel (the client paces them), deduped by work key.</summary>
    private async Task<IReadOnlyList<CatalogDoc>> SearchHypothesesAsync(
        IReadOnlyList<QueryHypothesis> hypotheses, CancellationToken cancellationToken)
    {
        var perHypothesis = await Task.WhenAll(hypotheses.Select(h => SearchHypothesisAsync(h, cancellationToken)));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return perHypothesis.SelectMany(docs => docs).Where(d => seen.Add(d.WorkKey)).ToList();
    }

    private async Task<IReadOnlyList<CatalogDoc>> SearchHypothesisAsync(QueryHypothesis h, CancellationToken cancellationToken)
    {
        if (h.IsFreeText) return await catalog.SearchAsync(new CatalogQuery(Text: h.FreeText, Limit: SearchLimit), cancellationToken);
        if (!h.HasTitle) return await catalog.SearchAsync(new CatalogQuery(Author: h.Author, Sort: "readinglog", Limit: SearchLimit), cancellationToken);

        var docs = await catalog.SearchAsync(new CatalogQuery(Title: h.Title, Author: h.Author, Limit: SearchLimit), cancellationToken);
        if (docs.Count == 0 && h.HasAuthor)
        {
            // Field search is strict about spelling; the general index is fuzzier.
            docs = await catalog.SearchAsync(new CatalogQuery(Text: $"{h.Title} {h.Author}", Limit: SearchLimit), cancellationToken);
        }
        return docs;
    }

    /// <summary>
    /// Provisional rank from search rows → fetch work records for the top few → re-classify with real
    /// primary authors → final rank.
    /// </summary>
    private async Task<IReadOnlyList<RankedCandidate>> RankAsync(
        IReadOnlyList<CatalogDoc> docs, IReadOnlyList<QueryHypothesis> hypotheses, CancellationToken cancellationToken)
    {
        var provisional = docs
            .Select(d => TierClassifier.Classify(Provisional(d), hypotheses))
            .OfType<RankedCandidate>();
        var shortlist = Ranker.Rank(CollapseDuplicateWorks(provisional), WorkRecordsToFetch);

        var docsByKey = docs.ToDictionary(d => d.WorkKey, StringComparer.Ordinal);
        var resolved = await Task.WhenAll(shortlist.Select(r => ResolveAsync(docsByKey[r.Book.WorkKey], cancellationToken)));

        var classified = resolved.Select(c => TierClassifier.Classify(c, hypotheses)).OfType<RankedCandidate>().ToList();
        return Ranker.Rank(TierClassifier.DemoteDerivatives(classified));
    }

    /// <summary>
    /// Tier D: an author's most-read works where they're a primary author on the work record, plus the author
    /// itself (Open Library key and name) for the "all works by …" link.
    /// </summary>
    private async Task<(IReadOnlyList<RankedCandidate> Works, AuthorRef? Author)> AuthorWorksAsync(string authorName, CancellationToken cancellationToken)
    {
        var hits = await catalog.FindAuthorsAsync(authorName, cancellationToken);
        var author = hits.Where(a => AuthorMatcher.Matches(authorName, a.Name)).MaxBy(a => a.WorkCount);

        var query = author is not null
            ? new CatalogQuery(AuthorKey: author.Key, Sort: "readinglog", Limit: SearchLimit)
            : new CatalogQuery(Author: authorName, Sort: "readinglog", Limit: SearchLimit);
        var docs = await catalog.SearchAsync(query, cancellationToken);

        // Most-read first; skip duplicate work records of the same title.
        var seenTitles = new HashSet<string>(StringComparer.Ordinal);
        var distinct = docs.Where(d => seenTitles.Add(TitleVariants.Base(d.Title))).Take(WorkRecordsToFetch);
        var resolved = await Task.WhenAll(distinct.Select(d => ResolveAsync(d, cancellationToken)));
        var name = author?.Name ?? authorName;
        var works = Ranker.Rank(resolved.Select(c => TierClassifier.ClassifyAuthorWork(c, name)).OfType<RankedCandidate>());

        // Prefer the resolved author; without one, the matched author on the top work usually carries its key.
        var resolvedAuthor = author is not null ? new AuthorRef(author.Key, author.Name) : works.FirstOrDefault()?.Evidence.MatchedAuthor;
        return (works, resolvedAuthor);
    }

    /// <summary>
    /// Keyword search for a query with no title or author. Kept in Open Library's relevance order (the best
    /// signal we have), duplicates collapsed, top few resolved, all tier E and flagged as keyword-only.
    /// </summary>
    private async Task<IReadOnlyList<RankedCandidate>> KeywordGuessesAsync(QueryHypothesis hypothesis, CancellationToken cancellationToken)
    {
        var keywords = QueryWords.Meaningful(hypothesis.Keywords);
        if (keywords.Count == 0) return [];

        var docs = await catalog.SearchAsync(new CatalogQuery(Text: string.Join(' ', keywords), Limit: SearchLimit), cancellationToken);
        var seen = new HashSet<(string, string)>();
        var distinct = docs
            .Where(d => seen.Add((TitleVariants.Base(d.Title), TextNormalizer.Normalize(d.AuthorNames.FirstOrDefault()))))
            .Take(WorkRecordsToFetch);

        var resolved = await Task.WhenAll(distinct.Select(d => ResolveAsync(d, cancellationToken)));
        return resolved
            .Select(book => new RankedCandidate(book, MatchTier.E, new MatchEvidence(
                TitleMatch.None, AuthorRole.None, null, YearMatched: false, HypothesisIndex: 0, KeywordOnly: true)))
            .ToList();
    }

    /// <summary>
    /// Open Library often has several work records for one book (five "Adventures of Huckleberry Finn" by
    /// Mark Twain; "… - Ukrainian Edition"). Keep the strongest, most-read one per base title + lead author:
    /// the canonical work.
    /// </summary>
    public static IEnumerable<RankedCandidate> CollapseDuplicateWorks(IEnumerable<RankedCandidate> candidates) =>
        candidates
            .GroupBy(c => (
                TitleVariants.Base(c.Book.Title),
                TextNormalizer.Normalize(c.Book.LeadAuthor?.Name)))
            .Select(g => g.Order(Ranker.Comparer).First());

    /// <summary>
    /// The fallback parser doesn't know whether "dickens" is an author. Treat it as one when every word
    /// belongs to the top result's lead author's name.
    /// </summary>
    private static string? FreeTextAuthor(QueryHypothesis hypothesis, IReadOnlyList<CatalogDoc> docs)
    {
        var lead = docs.FirstOrDefault()?.AuthorNames.FirstOrDefault();
        if (lead is null) return null;

        var nameTokens = TextNormalizer.Tokens(lead);
        var queryTokens = TextNormalizer.Tokens(hypothesis.FreeText);
        return queryTokens.Length > 0 && nameTokens.Length > 0 && queryTokens.Contains(nameTokens[^1]) && queryTokens.All(nameTokens.Contains)
            ? lead
            : null;
    }

    /// <summary>Before the work record is fetched, every listed name counts as a primary author.</summary>
    private static BookCandidate Provisional(CatalogDoc d) => new(
        d.WorkKey, d.Title, d.Subtitle,
        d.AuthorNames.Select((n, i) => new AuthorRef(i < d.AuthorKeys.Count ? d.AuthorKeys[i] : null, n)).ToList(),
        [], d.FirstPublishYear, d.CoverId, d.EditionCount, d.ReadingLogCount);

    /// <summary>Splits a search row's names into primary authors (on the work record) and contributors (everyone else).</summary>
    private async Task<BookCandidate> ResolveAsync(CatalogDoc doc, CancellationToken cancellationToken)
    {
        var work = await catalog.GetWorkAsync(doc.WorkKey, cancellationToken);
        if (work is null || work.AuthorKeys.Count == 0) return Provisional(doc);

        var namesByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(doc.AuthorKeys.Count, doc.AuthorNames.Count); i++)
            namesByKey.TryAdd(doc.AuthorKeys[i], doc.AuthorNames[i]);

        var primary = new List<AuthorRef>();
        foreach (var key in work.AuthorKeys.Distinct())
        {
            var name = namesByKey.GetValueOrDefault(key) ?? await catalog.GetAuthorNameAsync(key, cancellationToken);
            if (name is not null) primary.Add(new AuthorRef(key, name));
        }
        if (primary.Count == 0) return Provisional(doc);

        var primaryKeys = primary.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
        var contributors = namesByKey.Where(kv => !primaryKeys.Contains(kv.Key)).Select(kv => new AuthorRef(kv.Key, kv.Value)).ToList();

        return new BookCandidate(
            doc.WorkKey, doc.Title, doc.Subtitle ?? work.Subtitle, primary, contributors,
            doc.FirstPublishYear, doc.CoverId ?? work.CoverId, doc.EditionCount, doc.ReadingLogCount);
    }

    /// <summary>
    /// One batched Gemini call; any missing, empty or overlong sentence falls back to its template.
    /// Skipped when parsing just fell back: Gemini is struggling, so don't spend more time or quota on it.
    /// </summary>
    private async Task<(IReadOnlyList<SearchResult> Results, bool FromLlm, LlmFailureReason? Failure)> ExplainAsync(
        string query, IReadOnlyList<RankedCandidate> ranked, IReadOnlyList<QueryHypothesis> hypotheses, bool tryLlm, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> written = new Dictionary<string, string>();
        LlmFailureReason? failure = null;
        if (tryLlm)
        {
            try
            {
                written = await explanationWriter.WriteAsync(query, ranked, cancellationToken);
            }
            catch (LlmUnavailableException ex)
            {
                logger.LogWarning("LLM explanations unavailable ({LlmFailure}), using templates: {Reason}", ex.Reason, ex.Message);
                failure = ex.Reason;
            }
        }

        var allFromLlm = true;
        var results = ranked.Select((r, i) =>
        {
            if (written.TryGetValue(r.Book.WorkKey, out var text) && !string.IsNullOrWhiteSpace(text) && text.Length <= MaxExplanationLength)
                return new SearchResult(r, text.Trim());

            allFromLlm = false;
            var hypothesis = r.Evidence.HypothesisIndex < hypotheses.Count ? hypotheses[r.Evidence.HypothesisIndex] : null;
            return new SearchResult(r, TemplateExplanations.For(r, hypothesis, i));
        }).ToList();

        return (results, allFromLlm, failure);
    }
}
