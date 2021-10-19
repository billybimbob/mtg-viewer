using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Areas.Identity.Data;


[assembly: HostingStartup(typeof(MTGViewer.Areas.Identity.IdentityHostingStartup))]
namespace MTGViewer.Areas.Identity
{
    public class IdentityHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) => {
                var config = context.Configuration;
                var provider = config.GetValue("Provider", "Sqlite");

                services.AddDbContext<UserDbContext>(options =>
                {
                    switch (provider)
                    {
                        case "SqlServer":
                            // TODO: change connection string name
                            options.UseSqlServer(config.GetConnectionString("MTGCardContext"));
                            break;

                        case "Sqlite":
                        default:
                            options.UseSqlite(config.GetConnectionString("MTGCardContext"));
                            break;
                    }

                });

                services.AddDefaultIdentity<CardUser>(options => options.SignIn.RequireConfirmedAccount = true)
                    .AddEntityFrameworkStores<UserDbContext>();

                services.AddHostedService<UserSetup>();
            });
        }
    }


    internal class UserSetup : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public UserSetup(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }


        public async Task StartAsync(CancellationToken cancel)
        {
            using var scope = _serviceProvider.CreateScope();
            var scopeProvider = scope.ServiceProvider;

            var userDb = scopeProvider.GetRequiredService<UserDbContext>();

            await userDb.Database.MigrateAsync();
        }


        public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;
    }
}