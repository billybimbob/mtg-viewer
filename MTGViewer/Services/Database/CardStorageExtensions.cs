using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MTGViewer.Data;
using MTGViewer.Data.Triggers;
using MTGViewer.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class CardStorageExtensions
{
    public static IServiceCollection AddCardStorage(this IServiceCollection services, IConfiguration config)
    {
        var databaseOptions = DatabaseOptions.Bind(config);
        string connString = databaseOptions.GetConnectionString(config, DatabaseContext.Card);

        switch (databaseOptions.Provider)
        {
            case DatabaseOptions.SqlServer:
                services.AddDbContextFactory<CardDbContext>(options => options
                    .UseSqlServer(connString)
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints());
                break;

            case DatabaseOptions.Postgresql:
                services.AddPooledDbContextFactory<CardDbContext>(options => options
                    .UseNpgsql(connString.ToNpgsqlConnectionString())
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints());
                break;

            case DatabaseOptions.Sqlite:
            default:
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                    .UseSqlite(connString)
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints()
                    .UseTriggers(triggers => triggers
                        .AddTrigger<LiteTokenUpdate>()));
                break;
        }

        services.AddScoped<CardDbContext>(provider => provider
            .GetRequiredService<IDbContextFactory<CardDbContext>>()
            .CreateDbContext());

        services.AddHostedService<CardSetup>();

        return services;
    }
}