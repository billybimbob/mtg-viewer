using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.AspNetCore.Identity.UI.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;


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
                            options.UseSqlServer(config.GetConnectionString("SqlServerContext"));
                            break;

                        case "Sqlite":
                        default:
                            options.UseSqlite(config.GetConnectionString("SqliteContext"));
                            break;
                    }

                });

                services.AddDefaultIdentity<CardUser>(options => options.SignIn.RequireConfirmedAccount = true)
                    .AddEntityFrameworkStores<UserDbContext>();

                services.AddTransient<IEmailSender, EmailSender>();
                services.Configure<AuthMessageSenderOptions>(config);

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
            await using var scope = _serviceProvider.CreateAsyncScope();
            var scopeProvider = scope.ServiceProvider;

            var userDb = scopeProvider.GetRequiredService<UserDbContext>();

            await userDb.Database.MigrateAsync(cancel);
        }


        public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;
    }
}