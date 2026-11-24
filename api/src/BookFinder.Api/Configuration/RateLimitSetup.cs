using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace BookFinder.Api.Configuration;

/// <summary>
/// Per-client limit on searches, so one visitor can't burn the shared Gemini and Open Library quotas.
/// Over the limit → 429 ProblemDetails ("Lots of searches at once") with Retry-After, which the UI shows as a card.
/// </summary>
public static class RateLimitSetup
{
    public const string SearchPolicy = "search";
    public const string ConfigKey = "SEARCH_RATE_LIMIT_PER_MINUTE";
    public const int DefaultPerMinute = 10;

    public static IServiceCollection AddSearchRateLimiting(this IServiceCollection services)
    {
        // Render (and most hosts) sit behind a proxy; the client's address arrives in X-Forwarded-For.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(SearchPolicy, context =>
            {
                var config = context.RequestServices.GetRequiredService<IConfiguration>();
                var perMinute = int.TryParse(config[ConfigKey], out var n) && n > 0 ? n : DefaultPerMinute;
                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 10;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = retryAfter.ToString();

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Lots of searches at once",
                        Detail = "Try again in a few seconds.",
                        Type = "https://httpstatuses.io/429",
                    },
                });
            };
        });
        return services;
    }
}
