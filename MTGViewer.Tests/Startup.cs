using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests;


public class Startup
{
    public void ConfigureHost(IHostBuilder hostBuilder)
    {
        hostBuilder
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: true);
                config.AddUserSecrets<MTGViewer.Program>();
            });
    }


    public void ConfigureServices(IServiceCollection services, HostBuilderContext context)
    {
        var config = context.Configuration;
        var provider = config.GetValue("Provider", "InMemory");

        services.AddScoped<InMemoryConnection>();
        services.AddScoped<TempFileName>();

        switch (provider)
        {
            case "Sqlite":
                services.AddDbContext<CardDbContext>(TestFactory.SqliteInMemory);
                services.AddDbContext<UserDbContext>(TestFactory.SqliteInMemory);
                break;
            
            case "InMemory":
            default:
                services.AddDbContext<CardDbContext>(TestFactory.InMemoryDatabase);
                services.AddDbContext<UserDbContext>(TestFactory.InMemoryDatabase);
                break;
        }

        services.AddDbContextFactory<CardDbContext>((provider, options) => 
            // used scoped so that the db referenced is the locally scoped one
            provider.GetRequiredService<CardDbContext>(), ServiceLifetime.Scoped);

        services.AddScoped<ITreasuryQuery, SortedPartitionTreasury>();
        services.AddSingleton<PageSizes>();

        services.AddScoped<UserManager<CardUser>>(TestFactory.CardUserManager);
        services.AddScoped<SignInManager<CardUser>>(TestFactory.CardSignInManager);

        services.AddSymbols(options => options
            .AddFormatter<CardText>()
            .AddTranslator<ManaTranslator>());

        services.AddSingleton<IMemoryCache>(
            // should auto evict from limit
            new MemoryCache(
                new MemoryCacheOptions
                { 
                    SizeLimit = config.GetValue("CacheLimit", 100L) 
                }));

        services.AddSingleton<DataCacheService>();

        services.AddSingleton<IMtgServiceProvider, MtgServiceProvider>();

        services.AddScoped<ICardService>(provider => provider
            .GetRequiredService<IMtgServiceProvider>()
            .GetCardService());

        services.AddScoped<MTGFetchService>();

        services.AddScoped<FileCardStorage>();
        services.AddScoped<CardDataGenerator>();
        services.AddScoped<TestDataGenerator>();

        services.AddTransient<IEmailSender, EmailSender>();
        services.Configure<AuthMessageSenderOptions>(config);
    }
}
