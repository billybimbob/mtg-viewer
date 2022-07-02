using MtgApiManager.Lib.Service;

using MtgViewer.Services.Search;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddMtgQueries(this IServiceCollection services)
    {
        services
            .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
            .AddScoped(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

        return services
            .AddScoped<IMtgQuery, MtgApiQuery>()
            .AddScoped<IMtgCardSearch, MtgCardSearch>()
            .AddScoped<MtgApiFlipQuery>();
    }
}
