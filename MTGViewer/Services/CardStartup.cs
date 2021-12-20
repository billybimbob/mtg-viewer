using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Services;
using MTGViewer.Data;
using MTGViewer.Data.Triggers;


namespace Microsoft.Extensions.DependencyInjection
{
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
}


namespace MTGViewer.Services
{
    internal class CardSetup : IHostedService
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

            // migrate all here so that no concurrent migrations occur

            var userContext = scopeProvider.GetRequiredService<UserDbContext>();
            await userContext.Database.MigrateAsync(cancel);

            var dbContext = scopeProvider.GetRequiredService<CardDbContext>();
            await dbContext.Database.MigrateAsync(cancel);

            if (!_env.IsDevelopment())
            {
                return;
            }

            bool anyCards = await dbContext.Cards.AnyAsync(cancel);
            if (anyCards)
            {
                return;
            }

            var fileStorage = scopeProvider.GetRequiredService<FileCardStorage>();

            bool jsonSuccess = await fileStorage.TryJsonSeedAsync(cancel: cancel);
            if (!jsonSuccess)
            {
                var cardGen = scopeProvider.GetService<CardDataGenerator>();

                if (cardGen == null)
                {
                    return;
                }

                await cardGen.GenerateAsync(cancel);
                await fileStorage.WriteJsonAsync(cancel: cancel);
            }
        }


        public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;
    }
}