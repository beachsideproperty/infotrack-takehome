using BookFinder.Api.Configuration;
using BookFinder.Api.Contracts;
using BookFinder.Application.Search;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BookFinder.Api.Endpoints;

public static class SearchEndpoints
{
    public const string EmptyQueryMessage = "Type a title, an author or a few words you remember.";
    public const string TooLongMessage = "That's a lot to go on. Try the few words you're surest about.";

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", SearchAsync)
            .WithName("Search")
            .WithTags("Search")
            .RequireRateLimiting(RateLimitSetup.SearchPolicy)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    private static async Task<Results<Ok<SearchResponse>, ValidationProblem>> SearchAsync(
        SearchRequest? request, SearchService search, CancellationToken cancellationToken)
    {
        var query = request?.Query?.Trim() ?? string.Empty;
        var error = query.Length switch
        {
            0 => EmptyQueryMessage,
            > SearchService.MaxQueryLength => TooLongMessage,
            _ => null,
        };
        if (error is not null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["query"] = [error] }, detail: error);

        var outcome = await search.SearchAsync(query, cancellationToken);
        return TypedResults.Ok(outcome.ToResponse());
    }
}
