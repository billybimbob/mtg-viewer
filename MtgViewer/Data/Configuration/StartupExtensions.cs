using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MtgViewer.Data;
using MtgViewer.Data.Access;
using MtgViewer.Triggers;
using MtgViewer.Utils;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddCardStorage(this IServiceCollection services, IConfiguration config, Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        string connString = config.GetConnectionString("Cards");

        _ = config.GetConnectionString("Provider") switch
        {
            "Postgresql" => services
                .AddTriggeredPooledDbContextFactory<CardDbContext>(options =>
                {
                    options
                        .UseNpgsql(connString.ToNpgsqlConnectionString())
                        .UseValidationCheckConstraints()
                        .UseEnumCheckConstraints()
                        .UseTriggers(triggers => triggers
                            .AddTrigger<ColorUpdate>()
                            .AddTrigger<ImmutableCard>()
                            .AddTrigger<QuantityValidate>()
                            .AddTrigger<TradeValidate>());

                    configureOptions?.Invoke(options);
                }),

            "SqlServer" => services
                .AddTriggeredDbContextFactory<CardDbContext>(options =>
                {
                    options
                        .UseSqlServer(connString)
                        .UseValidationCheckConstraints()
                        .UseEnumCheckConstraints()
                        .UseTriggers(triggers => triggers
                            .AddTrigger<ColorUpdate>()
                            .AddTrigger<ImmutableCard>()
                            .AddTrigger<QuantityValidate>()
                            .AddTrigger<TradeValidate>());

                    configureOptions?.Invoke(options);
                }),

            "Sqlite" or _ => services
                .AddTriggeredDbContextFactory<CardDbContext>(options =>
                {
                    options
                        .UseSqlite(connString)
                        .UseValidationCheckConstraints()
                        .UseEnumCheckConstraints()
                        .UseTriggers(triggers => triggers
                            .AddTrigger<ColorUpdate>()
                            .AddTrigger<ImmutableCard>()
                            .AddTrigger<StampUpdate>()
                            .AddTrigger<QuantityValidate>()
                            .AddTrigger<TradeValidate>());

                    configureOptions?.Invoke(options);
                })
        };

        _ = services
            .AddScoped(provider => provider
                .GetRequiredService<IDbContextFactory<CardDbContext>>()
                .CreateDbContext());

        services.AddSingleton<ICardRepository, CardRepository>();

        return services;
    }
}
