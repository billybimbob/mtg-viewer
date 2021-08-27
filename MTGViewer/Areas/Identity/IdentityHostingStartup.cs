using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MTGViewer.Data;
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
            });
        }
    }
}