using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;
using MTGViewer.Data.Configuration;
using MTGViewer.Utils;

[assembly: HostingStartup(typeof(MTGViewer.Areas.Identity.IdentityHostingStartup))]
namespace MTGViewer.Areas.Identity;

public class IdentityHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices((context, services) =>
        {
            var config = context.Configuration;
            var databaseOptions = DatabaseOptions.Bind(config);

            string connString = databaseOptions.GetConnectionString(DatabaseContext.User);

            services.AddDbContext<UserDbContext>(options =>
            {
                switch (databaseOptions.Provider)
                {
                    case DatabaseOptions.SqlServer:
                        options.UseSqlServer(connString);
                        break;

                    case DatabaseOptions.Postgresql:
                        options.UseNpgsql(connString.ToNpgsqlConnectionString());
                        break;

                    case DatabaseOptions.Sqlite:
                    default:
                        options.UseSqlite(connString);
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
                .AddClaimsPrincipalFactory<CardUserClaimsPrincipalFactory>()
                .AddEntityFrameworkStores<UserDbContext>();

            services
                .AddDataProtection()
                .PersistKeysToDbContext<UserDbContext>();

            services.AddScoped<ReferenceManager>();

            services
                .Configure<AuthMessageSenderOptions>(config)
                .AddTransient<IEmailSender, EmailSender>()
                .AddTransient<EmailVerification>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy(CardPolicies.ChangeTreasury,
                    p => p.RequireClaim(CardClaims.ChangeTreasury));
            });
        });
    }
}
