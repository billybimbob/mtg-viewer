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

        config.GetSection(nameof(PrintingOptions)).Bind(options);

        if (options.UseLocal && options.FilePath is not null)
        {
            services.AddDbContextFactory<AllPrintingsDbContext>(optionsBuilder =>
            {
                optionsBuilder.UseSqlite(options.FilePath);
            });

            services.AddScoped<IMtgQuery, MtgAllPrintings>();
        }
        else
        {
            services
                .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
                .AddScoped(provider => provider
                    .GetRequiredService<IMtgServiceProvider>()
                    .GetCardService());

            services
                .AddScoped<IMtgQuery, MtgApiQuery>();
        }

        return services;
    }
}
