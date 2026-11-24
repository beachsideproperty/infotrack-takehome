using BookFinder.Application.Ports;
using Microsoft.AspNetCore.Diagnostics;

namespace BookFinder.Api.Errors;

/// <summary>
/// Maps known failures to ProblemDetails:
/// unreadable request body → 400; Open Library rate-limiting us → 503 (+ Retry-After); Open Library down → 502.
/// Anything else falls through to the default 500.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException bad =>
                (bad.StatusCode, "Invalid request", "Send JSON like {\"query\": \"tolkien hobbit\"}."),
            CatalogUnavailableException { RateLimited: true } =>
                (StatusCodes.Status503ServiceUnavailable, "Open Library is busy", "Open Library is limiting requests. Try again in a few seconds."),
            CatalogUnavailableException =>
                (StatusCodes.Status502BadGateway, "Can't reach Open Library right now", "The book catalog didn't answer in time. Try again."),
            _ => (0, null, null),
        };
        if (status == 0) return false;

        logger.LogWarning("Request failed with {Status}: {Reason}", status, exception.Message);
        context.Response.StatusCode = status;
        if (status == StatusCodes.Status503ServiceUnavailable) context.Response.Headers.RetryAfter = "5";

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail, Type = $"https://httpstatuses.io/{status}" },
        });
    }
}
