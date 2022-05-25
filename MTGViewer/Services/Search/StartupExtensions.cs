using MtgApiManager.Lib.Service;

using MTGViewer.Services.Search;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddMtgQueries(this IServiceCollection services)
    {
        _ = services
            .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
            .AddScoped(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

        return services
            .AddScoped<IMTGQuery, MtgApiQuery>()
            .AddScoped<MtgApiFlipQuery>();
    }
}
