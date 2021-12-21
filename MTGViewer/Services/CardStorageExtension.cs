using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MTGViewer.Data;
using MTGViewer.Data.Triggers;
using MTGViewer.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class CardStorageExtension
{
    public static IServiceCollection AddCardStorage(
        this IServiceCollection services, IConfiguration config)
    {
        var provider = config.GetValue("Provider", "Sqlite");

        switch (provider)
        {
            case "SqlServer":
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                        // TODO: change connection string name
                    .UseSqlServer(config.GetConnectionString("SqlServer"))
                    .UseTriggers(triggers => triggers
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>() ));
                break;

            case "Postgresql":
                string remoteKey = config["RemoteKey"];

                services.AddTriggeredPooledDbContextFactory<CardDbContext>(options => options
                    .UseNpgsql(config[remoteKey])
                    .UseTriggers(triggers => triggers
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>() ));
                break;

            case "Sqlite":
            default:
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                    .UseSqlite(config.GetConnectionString("Sqlite"))
                    .UseTriggers(triggers => triggers
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<LiteTokenUpdate>()
                        .AddTrigger<TradeValidate>() ));
                break;
        }

        services.AddScoped<CardDbContext>(provider => provider
            .GetRequiredService<IDbContextFactory<CardDbContext>>()
            .CreateDbContext());

        services.AddHostedService<CardSetup>();

        return services;
    }
}