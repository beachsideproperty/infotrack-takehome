using BookFinder.Application.Search;
using Microsoft.Extensions.DependencyInjection;

namespace BookFinder.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SearchService>();
        return services;
    }
}
