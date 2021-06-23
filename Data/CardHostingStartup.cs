using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

#if SQLiteVersion
using MTGViewer.Data.Concurrency;
#endif


[assembly: HostingStartup(typeof(MTGViewer.Data.CardHostingStartup))]
namespace MTGViewer.Data
{
    public class CardHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder webBuilder)
        {
            webBuilder.ConfigureServices((context, services) => {

#if SQLiteVersion
                services.AddTriggeredDbContextFactory<MTGCardContext>(options => options
                    .UseSqlite(context.Configuration.GetConnectionString("MTGCardContext"))
                    .UseTriggers(triggers => triggers
                        .AddTrigger<GuidTokenTrigger>()) );
#else                    
                services.AddDbContextFactory<MTGCardContext>(options => options
                    .UseSqlServer(context.Configuration.GetConnectionString("MTGCardContext")) );
#endif

                services.AddScoped<MTGCardContext>(provider => provider
                    .GetRequiredService<IDbContextFactory<MTGCardContext>>()
                    .CreateDbContext());
            });

        }
    }
}