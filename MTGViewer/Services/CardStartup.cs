using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Data;
using MTGViewer.Data.Triggers;

namespace MTGViewer.Services;

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
                    .UseSqlServer(config.GetConnectionString("MTGCardContext"))
                    .UseTriggers(triggers => triggers
                        .AddTrigger<QuantityValidate>()
                        .AddTrigger<TradeValidate>() ));
                break;

            case "Sqlite":
            default:
                services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                    .UseSqlite(config.GetConnectionString("MTGCardContext"))
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


public class CardSetup : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebHostEnvironment _env;

    public CardSetup(IServiceProvider serviceProvider, IWebHostEnvironment env)
    {
        _serviceProvider = serviceProvider;
        _env = env;
    }


    public async Task StartAsync(CancellationToken cancel)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var scopeProvider = scope.ServiceProvider;

        var dbContext = scopeProvider.GetRequiredService<CardDbContext>();
        await dbContext.Database.MigrateAsync(cancel);

        if (!_env.IsDevelopment())
        {
            return;
        }

        var anyCards = await dbContext.Cards.AnyAsync(cancel);

        if (anyCards)
        {
            return;
        }

        var jsonStorage = scopeProvider.GetRequiredService<JsonCardStorage>();
        var jsonSuccess = await jsonStorage.SeedFromJsonAsync(cancel: cancel);

        if (!jsonSuccess)
        {
            var cardGen = scopeProvider.GetService<CardDataGenerator>();

            if (cardGen == null)
            {
                return;
            }

            await cardGen.GenerateAsync(cancel);
            await jsonStorage.WriteToJsonAsync(cancel: cancel);
        }

        // var treasury = scopeProvider.GetRequiredService<ITreasury>();

        // await treasury.OptimizeAsync();
    }


    public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;

}