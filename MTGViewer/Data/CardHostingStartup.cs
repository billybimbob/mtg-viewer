using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Seed;
using MTGViewer.Services;


[assembly: HostingStartup(typeof(MTGViewer.Data.CardHostingStartup))]
namespace MTGViewer.Data
{
    public class CardHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder webBuilder)
        {
            webBuilder.ConfigureServices((context, services) =>
            {
                var config = context.Configuration;
                var provider = config.GetValue("Provider", "Sqlite");

                switch (provider)
                {
                    case "SqlServer":
                        services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                                // TODO: change connection string name
                            .UseSqlServer(config.GetConnectionString("MTGCardContext"))
                            .UseTriggers(triggers => triggers
                                .AddTrigger<Triggers.AmountValidate>()
                                .AddTrigger<Triggers.RequestValidate>()
                                .AddTrigger<Triggers.TradeValidate>() ));
                        break;

                    case "Sqlite":
                    default:
                        services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                            .UseSqlite(config.GetConnectionString("MTGCardContext"))
                            .UseTriggers(triggers => triggers
                                .AddTrigger<Triggers.AmountValidate>()
                                .AddTrigger<Triggers.LiteTokenUpdate>()
                                .AddTrigger<Triggers.RequestValidate>()
                                .AddTrigger<Triggers.TradeValidate>() ));
                        break;
                }

                services.AddScoped<CardDbContext>(provider => provider
                    .GetRequiredService<IDbContextFactory<CardDbContext>>()
                    .CreateDbContext());

                services.AddHostedService<CardSetup>();
            });
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
            using var scope = _serviceProvider.CreateScope();
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

            var sharedStorage = scopeProvider.GetRequiredService<ISharedStorage>();
            var userManager = scopeProvider.GetRequiredService<UserManager<CardUser>>();
            var fetchService = scopeProvider.GetRequiredService<MTGFetchService>();

            var cardGen = new CardDataGenerator(dbContext, sharedStorage, userManager, fetchService);
            var jsonSuccess = await cardGen.AddFromJsonAsync(cancel: cancel);

            if (!jsonSuccess)
            {
                await cardGen.GenerateAsync(cancel);
                await cardGen.WriteToJsonAsync(cancel: cancel);
            }
            
            await sharedStorage.OptimizeAsync();
        }


        public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;
    }

}