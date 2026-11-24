using Microsoft.AspNetCore.Cors.Infrastructure;

namespace BookFinder.Api.Configuration;

public static class CorsSetup
{
    public const string ConfigKey = "ALLOWED_ORIGINS";

    /// <summary>
    /// Default CORS policy from ALLOWED_ORIGINS (comma-separated, e.g. "https://app.vercel.app,http://localhost:5173").
    /// Read lazily through options so tests and env vars can override it.
    /// </summary>
    public static IServiceCollection AddConfiguredCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IConfiguration, ILoggerFactory>((options, config, loggerFactory) =>
        {
            var origins = ParseOrigins(config[ConfigKey]);
            if (origins.Length == 0)
            {
                loggerFactory.CreateLogger(typeof(CorsSetup))
                    .LogWarning("{Key} is empty; browsers on other origins will be blocked by CORS.", ConfigKey);
            }

            options.AddDefaultPolicy(policy => policy
                .WithOrigins(origins)
                .WithMethods("GET", "POST")
                .WithHeaders("Content-Type"));
        });
        return services;
    }

    public static string[] ParseOrigins(string? raw) =>
        (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
