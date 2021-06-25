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
#if SQLiteVersion
                services.AddTriggeredDbContextFactory<MTGCardContext>(options => options
                    .UseSqlite(context.Configuration.GetConnectionString("MTGCardContext"))
                    .UseTriggers(triggers => triggers
                        .AddTrigger<Triggers.GuidTokenTrigger>()
                        .AddTrigger<Triggers.RequestAmountTrigger>()) );
#else                    
                services.AddTriggerDbContextFactory<MTGCardContext>(options => options
                    .UseSqlServer(context.Configuration.GetConnectionString("MTGCardContext"))
                    // TODO: change connection string name
                    .UseTriggers(triggers => triggers
                        .AddTrigger<Triggers.RequestAmountTrigger>()) );
#endif

                services.AddScoped<MTGCardContext>(provider => provider
                    .GetRequiredService<IDbContextFactory<MTGCardContext>>()
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
                var contextFactory = serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<MTGCardContext>>();
                using var context = contextFactory.CreateDbContext();
                context.Database.EnsureCreated();

                if (env.IsDevelopment())
                {
                    AddDefaultLocation(context);
                }
            }

            return app;
        }


        private static void AddDefaultLocation(MTGCardContext context)
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