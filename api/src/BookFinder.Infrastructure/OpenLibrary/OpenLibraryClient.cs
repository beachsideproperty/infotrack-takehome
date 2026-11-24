using System.Net;
using System.Text;
using System.Text.Json;
using BookFinder.Application.Ports;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookFinder.Infrastructure.OpenLibrary;

/// <summary>
/// Typed client for the Open Library read APIs. Responses are cached by URL; network calls go through
/// <see cref="RateLimitingHandler"/>. Any transport failure surfaces as <see cref="CatalogUnavailableException"/>.
/// </summary>
public sealed class OpenLibraryClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<OpenLibraryOptions> options,
    ILogger<OpenLibraryClient> logger) : IBookCatalog
{
    public const string SearchFields =
        "key,title,subtitle,author_name,author_key,first_publish_year,cover_i,edition_count,readinglog_count";

    public async Task<IReadOnlyList<CatalogDoc>> SearchAsync(CatalogQuery query, CancellationToken cancellationToken)
    {
        var url = BuildSearchUrl(query);
        using var json = await GetJsonAsync(url, cancellationToken)
            ?? throw new CatalogUnavailableException($"Open Library search returned 404 for {url}.");

        if (!json.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array) return [];

        var results = new List<CatalogDoc>();
        foreach (var doc in docs.EnumerateArray())
        {
            var key = doc.Str("key");
            var title = doc.Str("title");
            if (key is null || title is null) continue;

            results.Add(new CatalogDoc(
                key,
                title,
                doc.Str("subtitle"),
                doc.StrArray("author_name"),
                doc.StrArray("author_key").Select(BareKey).ToList(),
                doc.Int("first_publish_year"),
                doc.Int("cover_i"),
                doc.Int("edition_count") ?? 0,
                doc.Int("readinglog_count") ?? 0));
        }
        return results;
    }

    public async Task<WorkRecord?> GetWorkAsync(string workKey, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"works/{BareKey(workKey)}.json", cancellationToken);
        if (json is null) return null;
        var root = json.RootElement;

        // authors: [{ "author": { "key": "/authors/OL26320A" }, "type": ... }]; very old records use "author": "/authors/…".
        var authorKeys = new List<string>();
        if (root.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in authors.EnumerateArray())
            {
                if (!entry.TryGetProperty("author", out var author)) continue;
                var key = author.ValueKind == JsonValueKind.String ? author.GetString() : author.Str("key");
                if (!string.IsNullOrEmpty(key)) authorKeys.Add(BareKey(key));
            }
        }

        int? cover = root.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array
            ? covers.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Number).Select(c => c.GetInt32()).FirstOrDefault(c => c > 0)
            : null;

        return new WorkRecord(workKey, root.Str("title") ?? string.Empty, root.Str("subtitle"), authorKeys, cover is > 0 ? cover : null);
    }

    public async Task<string?> GetAuthorNameAsync(string authorKey, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"authors/{BareKey(authorKey)}.json", cancellationToken);
        return json?.RootElement.Str("name") ?? json?.RootElement.Str("personal_name");
    }

    public async Task<IReadOnlyList<AuthorHit>> FindAuthorsAsync(string name, CancellationToken cancellationToken)
    {
        using var json = await GetJsonAsync($"search/authors.json?q={Uri.EscapeDataString(name)}&limit=5", cancellationToken);
        if (json is null || !json.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array) return [];

        return docs.EnumerateArray()
            .Select(d => (Key: d.Str("key"), Name: d.Str("name"), Works: d.Int("work_count") ?? 0))
            .Where(d => d.Key is not null && d.Name is not null)
            .Select(d => new AuthorHit(BareKey(d.Key!), d.Name!, d.Works))
            .ToList();
    }

    public static string BuildSearchUrl(CatalogQuery query)
    {
        var sb = new StringBuilder("search.json?");
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim())).Append('&');
        }

        Add("q", query.Text);
        Add("title", query.Title);
        Add("author", query.Author);
        Add("author_key", query.AuthorKey);
        Add("sort", query.Sort);
        Add("fields", SearchFields);
        sb.Append("limit=").Append(query.Limit);
        return sb.ToString();
    }

    /// <summary>"/authors/OL26320A" → "OL26320A".</summary>
    private static string BareKey(string key) => key[(key.LastIndexOf('/') + 1)..];

    /// <summary>GET relative URL → parsed JSON, cached by URL. Null on 404.</summary>
    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var cacheKey = "openlibrary:" + relativeUrl;
        if (!cache.TryGetValue(cacheKey, out string? body))
        {
            body = await FetchAsync(relativeUrl, cancellationToken);
            cache.Set(cacheKey, body, options.Value.CacheDuration);
        }

        if (body is null) return null;
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            cache.Remove(cacheKey);
            throw new CatalogUnavailableException("Open Library returned malformed JSON.", inner: ex);
        }
    }

    /// <summary>Open Library has occasional slow or failed responses; one quick retry rides out most of them.</summary>
    private async Task<string?> FetchAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await FetchOnceAsync(relativeUrl, cancellationToken);
        }
        catch (CatalogUnavailableException ex) when (!ex.RateLimited)
        {
            logger.LogInformation("Retrying Open Library once: {Url} ({Reason})", relativeUrl, ex.Message);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            return await FetchOnceAsync(relativeUrl, cancellationToken);
        }
    }

    private async Task<string?> FetchOnceAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(relativeUrl, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Open Library timed out: {Url}", relativeUrl);
            throw new CatalogUnavailableException("Open Library timed out.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Open Library request failed: {Url}", relativeUrl);
            throw new CatalogUnavailableException("Open Library is unreachable.", inner: ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new CatalogUnavailableException("Open Library is rate-limiting us.", rateLimited: true);
            if (!response.IsSuccessStatusCode)
                throw new CatalogUnavailableException($"Open Library returned {(int)response.StatusCode}.");

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }
}

internal static class JsonElementExtensions
{
    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int? Int(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static IReadOnlyList<string> StrArray(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];
}
