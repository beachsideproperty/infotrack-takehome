using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace BookFinder.Infrastructure.OpenLibrary;

/// <summary>Process-wide token bucket so all Open Library calls together stay under the published limit.</summary>
public sealed class OpenLibraryRateLimiter(IOptions<OpenLibraryOptions> options) : IDisposable
{
    public RateLimiter Limiter { get; } = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = options.Value.RequestsPerSecond,
        TokensPerPeriod = options.Value.RequestsPerSecond,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 200,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    });

    public void Dispose() => Limiter.Dispose();
}

/// <summary>Waits for a token before each network call. Cache hits never reach this handler.</summary>
public sealed class RateLimitingHandler(OpenLibraryRateLimiter rateLimiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await rateLimiter.Limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired) throw new HttpRequestException("Open Library request queue is full.");
        return await base.SendAsync(request, cancellationToken);
    }
}
