using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MtgApiManager.Lib.Service;

using MtgViewer.Services.Search;
using MtgViewer.Services.Search.Database;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddMtgQueries(this IServiceCollection services, IConfiguration config)
    {
        var options = new PrintingOptions();

        config.Bind(nameof(PrintingOptions), options);

        if (options.UseLocal && options.FilePath is not null)
        {
            services
                .AddScoped<IMtgQuery, MtgAllPrintings>()
                .AddDbContextFactory<AllPrintingsDbContext>(optionsBuilder =>
                {
                    optionsBuilder.UseSqlite(options.FilePath);
                });
        }
        else
        {
            services
                .AddScoped<IMtgQuery, MtgApiQuery>()
                .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
                .AddScoped(provider => provider
                    .GetRequiredService<IMtgServiceProvider>()
                    .GetCardService());
        }

        return services;
    }
}
