using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


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

                switch (provider) {

                case "SqlServer":

                    services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                        .UseSqlServer(config.GetConnectionString("MTGCardContext"))
                            // TODO: change connection string name
                        .UseTriggers(triggers => triggers
                            .AddTrigger<Triggers.RequestAmountTrigger>()) );
                    break;

                case "Sqlite":
                default:

                    services.AddTriggeredDbContextFactory<CardDbContext>(options => options
                        .UseSqlite(config.GetConnectionString("MTGCardContext"))
                        .UseTriggers(triggers => triggers
                            .AddTrigger<Triggers.GuidTokenTrigger>()
                            .AddTrigger<Triggers.RequestAmountTrigger>()) );
                    break;
                }

                services.AddScoped<CardDbContext>(provider => provider
                    .GetRequiredService<IDbContextFactory<CardDbContext>>()
                    .CreateDbContext());
            });
        }
    }


    public static class BuilderExtensions
    {
        public static IApplicationBuilder CheckDatabase(this IApplicationBuilder app, IWebHostEnvironment env)
        {
            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                var contextFactory = serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<CardDbContext>>();
                using var context = contextFactory.CreateDbContext();
                context.Database.EnsureCreated();

                if (env.IsDevelopment())
                {
                    AddDefaultLocation(context);
                }
            }

            return app;
        }


        private static void AddDefaultLocation(CardDbContext context)
        {
            if (context.Locations.Any())
            {
                return;
            }

            context.Locations.Add(new Location("Dev Default"));
            context.SaveChanges();
        }
    }

}