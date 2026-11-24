using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BookFinder.Tests.Support;

/// <summary>
/// Replays recorded Open Library responses from <c>Fixtures/openlibrary</c>, one file per URL.
/// With <c>RECORD_FIXTURES=1</c>, missing fixtures are fetched live (politely paced) and saved, so
/// <c>RECORD_FIXTURES=1 dotnet test --filter Category=Golden</c> refreshes them. A missing fixture
/// otherwise fails the test loudly rather than touching the network.
/// </summary>
public sealed partial class FixtureHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim Pace = new(1, 1);
    private readonly bool _record = Environment.GetEnvironmentVariable("RECORD_FIXTURES") == "1";

    public List<string> Requests { get; } = [];

    public FixtureHandler() : base(new HttpClientHandler()) { }

    public static string Directory([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", "openlibrary");

    public static string FileFor(Uri uri)
    {
        var pathAndQuery = Uri.UnescapeDataString(uri.PathAndQuery.TrimStart('/'));
        var readable = Unsafe().Replace(pathAndQuery.Replace("&fields=" + BookFinder.Infrastructure.OpenLibrary.OpenLibraryClient.SearchFields, ""), "-").Trim('-').ToLowerInvariant();
        if (readable.Length > 80) readable = readable[..80];
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.PathAndQuery)))[..8].ToLowerInvariant();
        return Path.Combine(Directory(), $"{readable}-{hash}.json");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(Uri.UnescapeDataString(request.RequestUri!.PathAndQuery));
        var file = FileFor(request.RequestUri!);

        if (File.Exists(file))
        {
            var body = await File.ReadAllTextAsync(file, cancellationToken);
            return body == "404"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        if (!_record)
            throw new InvalidOperationException($"No fixture for {request.RequestUri}. Record with RECORD_FIXTURES=1 dotnet test --filter Category=Golden.");

        await Pace.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(1100, cancellationToken); // unidentified clients get 1 req/s
            var response = await base.SendAsync(request, cancellationToken);
            System.IO.Directory.CreateDirectory(Directory());
            if (response.StatusCode == HttpStatusCode.NotFound) await File.WriteAllTextAsync(file, "404", cancellationToken);
            else if (response.IsSuccessStatusCode) await File.WriteAllTextAsync(file, await response.Content.ReadAsStringAsync(cancellationToken), cancellationToken);
            return response;
        }
        finally
        {
            Pace.Release();
        }
    }

    [GeneratedRegex("[^a-zA-Z0-9]+")]
    private static partial Regex Unsafe();
}

/// <summary>Scripted HTTP responses for client tests.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public List<HttpRequestMessage> Seen { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Seen.Add(request);
        return respond(request, ++Calls).WaitAsync(cancellationToken); // honour timeouts like a real handler
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
