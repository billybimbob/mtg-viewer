using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MtgViewer.Data;
using MtgViewer.Triggers;
using MtgViewer.Utils;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddCardStorage(this IServiceCollection services, IConfiguration config)
    {
        string connString = config.GetConnectionString("Cards");

        _ = config.GetConnectionString("Provider") switch
        {
            "Postgresql" => services
                .AddTriggeredPooledDbContextFactory<CardDbContext>(options => options

                    .UseNpgsql(connString.ToNpgsqlConnectionString())
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints()

                    .UseTriggers(triggers => triggers
                        .AddTrigger<ColorUpdate>()
                        .AddTrigger<ImmutableCard>()
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>())),

            "SqlServer" => services
                .AddTriggeredDbContextFactory<CardDbContext>(options => options

                    .UseSqlServer(connString)
                    .UseValidationCheckConstraints()
                    .UseEnumCheckConstraints()

                    .UseTriggers(triggers => triggers
                        .AddTrigger<ColorUpdate>()
                        .AddTrigger<ImmutableCard>()
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>())),

            "Sqlite" or _ => services
                .AddTriggeredDbContextFactory<CardDbContext>(options => options

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

        return services
            .AddScoped(provider => provider
                .GetRequiredService<IDbContextFactory<CardDbContext>>()
                .CreateDbContext());
    }
}
