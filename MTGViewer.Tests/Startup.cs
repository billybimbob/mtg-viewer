using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;
using MTGViewer.Areas.Identity.Data;
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
            });
    }


    public void ConfigureServices(IServiceCollection services, HostBuilderContext context)
    {
        var provider = context.Configuration.GetValue("Provider", "InMemory");

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

        services.AddScoped<ITreasury, FlatVariableStorage>();
        services.AddScoped<UserManager<CardUser>>(TestFactory.CardUserManager);

        services.AddSingleton<PageSizes>();

        services.AddSymbols(options => options
            .AddFormatter<CardText>()
            .AddTranslator<ManaTranslator>());

        services.AddSingleton<DataCacheService>();
        services.AddSingleton<IMtgServiceProvider, MtgServiceProvider>();

        services.AddScoped<ICardService>(provider => provider
            .GetRequiredService<IMtgServiceProvider>()
            .GetCardService());

        services.AddScoped<MTGFetchService>();

        services.AddScoped<JsonCardStorage>();
        services.AddScoped<CardDataGenerator>();
        services.AddScoped<TestDataGenerator>();
    }
}