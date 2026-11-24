namespace BookFinder.Infrastructure.OpenLibrary;

public sealed class OpenLibraryOptions
{
    public Uri BaseAddress { get; set; } = new("https://openlibrary.org/");

    /// <summary>Sent in the User-Agent. Open Library allows 3 req/s for identified clients, 1 req/s otherwise.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Per attempt. A timed-out or 5xx request is retried once.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(6);

    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(6);

    public int RequestsPerSecond => string.IsNullOrWhiteSpace(ContactEmail) ? 1 : 3;

    public string UserAgent => string.IsNullOrWhiteSpace(ContactEmail)
        ? "BookFinder/1.0 (+https://github.com/beachsideproperty/infotrack-takehome)"
        : $"BookFinder/1.0 ({ContactEmail})";
}
