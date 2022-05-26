using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MtgViewer.Data;
using MtgViewer.Data.Configuration;
using MtgViewer.Triggers;
using MtgViewer.Utils;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddCardStorage(this IServiceCollection services, IConfiguration config)
    {
        var databaseOptions = DatabaseOptions.FromConfiguration(config);
        string connString = databaseOptions.GetConnectionString(DatabaseContext.Card);

        _ = databaseOptions.Provider switch
        {
            DatabaseOptions.SqlServer =>
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options

                    .UseSqlServer(connString)
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints()

                    .UseTriggers(triggers => triggers
                        .AddTrigger<ColorUpdate>()
                        .AddTrigger<ImmutableCard>()
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>())),

            DatabaseOptions.Postgresql =>
                services.AddTriggeredPooledDbContextFactory<CardDbContext>(options => options

                    .UseNpgsql(connString.ToNpgsqlConnectionString())
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints()

                    .UseTriggers(triggers => triggers
                        .AddTrigger<ColorUpdate>()
                        .AddTrigger<ImmutableCard>()
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>())),

            DatabaseOptions.Sqlite or _ =>
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options

                    .UseSqlite(connString)
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints()

                    .UseTriggers(triggers => triggers
                        .AddTrigger<ColorUpdate>()
                        .AddTrigger<ImmutableCard>()
                        .AddTrigger<StampUpdate>()
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>()))
        };

        return services.AddScoped(provider => provider
            .GetRequiredService<IDbContextFactory<CardDbContext>>()
            .CreateDbContext());
    }
}
