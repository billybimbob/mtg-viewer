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
                            options.UseSqlServer(config.GetConnectionString("SqlServer"));
                            break;

                        case "Sqlite":
                        default:
                            options.UseSqlite(config.GetConnectionString("Sqlite"));
                            break;
                    }
                });

                services
                    .AddDefaultIdentity<CardUser>(options =>
                    {
                        options.SignIn.RequireConfirmedAccount = true;
                        options.SignIn.RequireConfirmedEmail = true;
                        options.User.RequireUniqueEmail = true;
                    })
                    .AddEntityFrameworkStores<UserDbContext>();

                services.AddTransient<IEmailSender, EmailSender>();
                services.Configure<AuthMessageSenderOptions>(config);

                services.AddTransient<EmailVerification>();
                services.AddScoped<ReferenceManager>();
            });
        }
    }
}