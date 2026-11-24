using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookFinder.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers external clients (Gemini, Open Library) and caching. Filled in from phase 2.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration) => services;
}
