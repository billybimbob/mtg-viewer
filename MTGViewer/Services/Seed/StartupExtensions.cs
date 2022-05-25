using Microsoft.Extensions.Configuration;

using MTGViewer.Services.Infrastructure;
using MTGViewer.Services.Seed;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddCardSeedServices(this IServiceCollection services, IConfiguration config)
    {
        return services
            .Configure<SeedSettings>(config.GetSection(nameof(SeedSettings)))
            .AddScoped<SeedHandler>()
            .AddScoped<CardDataGenerator>()
            .AddHostedService<CardSeed>();
    }
}
