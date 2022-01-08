using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MTGViewer.Data;
using MTGViewer.Data.Triggers;
using MTGViewer.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class CardStorageExtensions
{
    public static IServiceCollection AddCardStorage(
        this IServiceCollection services, IConfiguration config)
    {
        string provider = config.GetValue("Provider", "Sqlite");

        string connString = provider switch
        {
            "SqlServer" => 
                config.GetConnectionString("SqlServer"),

            "Postgresql" when config["RemoteKey"] is var remoteKey => 
                config[remoteKey].ToNpgsqlConnectionString(),

            _ => 
                config.GetConnectionString("Sqlite")
        };

        switch (provider)
        {
            case "SqlServer":
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                    .UseSqlServer(connString)
                    .UseTriggers(triggers => triggers
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>() ));
                break;

            case "Postgresql":
                services.AddTriggeredPooledDbContextFactory<CardDbContext>(options => options
                    .UseNpgsql(connString)
                    .UseTriggers(triggers => triggers
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>() ));
                break;

            case "Sqlite":
            default:
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                    .UseSqlite(connString)
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