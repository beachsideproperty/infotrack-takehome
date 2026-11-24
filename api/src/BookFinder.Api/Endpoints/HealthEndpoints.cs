using System.Reflection;

namespace BookFinder.Api.Endpoints;

public static class HealthEndpoints
{
    public sealed record HealthResponse(string Status, string Version, string? Commit);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var version = Environment.GetEnvironmentVariable("APP_VERSION")
            ?? typeof(HealthEndpoints).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        // Render injects RENDER_GIT_COMMIT; handy for confirming which build is live.
        var commit = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") is { Length: >= 7 } sha ? sha[..7] : null;
        var response = new HealthResponse("ok", version, commit);

        app.MapGet("/health", (HttpContext http) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                return TypedResults.Ok(response);
            })
            .WithName("Health")
            .WithTags("Health");

        return app;
    }
}
