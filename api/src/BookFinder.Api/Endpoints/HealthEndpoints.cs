using System.Reflection;
using BookFinder.Infrastructure.Gemini;

namespace BookFinder.Api.Endpoints;

public static class HealthEndpoints
{
    /// <param name="Ai">Gemini as the server currently sees it (no call to Google). Searches still work when it isn't "ok".</param>
    public sealed record HealthResponse(string Status, string Version, string? Commit, GeminiStatus.Report Ai);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var version = Environment.GetEnvironmentVariable("APP_VERSION")
            ?? typeof(HealthEndpoints).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        // Render injects RENDER_GIT_COMMIT; handy for confirming which build is live.
        var commit = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") is { Length: >= 7 } sha ? sha[..7] : null;

        app.MapGet("/health", (HttpContext http, GeminiStatus gemini) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                // Always 200 while the API itself is up: Render's health check shouldn't restart us because Google is busy.
                return TypedResults.Ok(new HealthResponse("ok", version, commit, gemini.Current()));
            })
            .WithName("Health")
            .WithTags("Health");

        return app;
    }
}
