using BookFinder.Application.Ports;
using BookFinder.Infrastructure.Gemini;
using BookFinder.Infrastructure.OpenLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookFinder.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Open Library and Gemini clients. Settings come from flat env-style keys (see .env.example).</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.Configure<OpenLibraryOptions>(o => o.ContactEmail = configuration["OPENLIBRARY_CONTACT_EMAIL"]);
        services.AddSingleton<OpenLibraryRateLimiter>();
        services.AddTransient<RateLimitingHandler>();
        services.AddHttpClient<IBookCatalog, OpenLibraryClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpenLibraryOptions>>().Value;
                http.BaseAddress = options.BaseAddress;
                http.Timeout = options.Timeout;
                http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<RateLimitingHandler>();

        services.Configure<GeminiOptions>(o =>
        {
            o.ApiKey = configuration["GEMINI_API_KEY"];
            if (configuration["GEMINI_MODEL"] is { Length: > 0 } model) o.Model = model;
            // Empty means "use the default", so copying .env.example as-is never disables the fallback model.
            if (configuration["GEMINI_FALLBACK_MODEL"] is { Length: > 0 } fallback) o.FallbackModel = fallback;
            if (int.TryParse(configuration["GEMINI_RPM_PER_MODEL"], out var rpm) && rpm > 0) o.RequestsPerMinutePerModel = rpm;
        });
        services.AddSingleton<GeminiModelGate>();
        services.AddSingleton<GeminiStatus>();
        services.AddHttpClient<GeminiClient>((sp, http) =>
        {
            http.BaseAddress = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GeminiOptions>>().Value.BaseAddress;
            http.Timeout = Timeout.InfiniteTimeSpan; // GeminiClient enforces its own per-operation budget
        });
        services.AddScoped<ILlmQueryParser, GeminiQueryParser>();
        services.AddScoped<IExplanationWriter, GeminiExplanationWriter>();

        return services;
    }
}
