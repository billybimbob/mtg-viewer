using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;
using MtgViewer.Data.Configuration;
using MtgViewer.Utils;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddCardUsers(this IServiceCollection services, IConfiguration config)
    {
        var databaseOptions = DatabaseOptions.FromConfiguration(config);

        string connString = databaseOptions.GetConnectionString(DatabaseContext.User);

        services.AddDbContext<UserDbContext>(options =>
            _ = databaseOptions.Provider switch
            {
                DatabaseOptions.SqlServer =>
                    options.UseSqlServer(connString),

                DatabaseOptions.Postgresql =>
                    options.UseNpgsql(StringExtensions.ToNpgsqlConnectionString(connString)),

                DatabaseOptions.Sqlite or _ =>
                    options.UseSqlite(connString)
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

        services.AddScoped<PlayerManager>();

        services
            .Configure<AuthMessageSenderOptions>(config)
            .AddTransient<IEmailSender, EmailSender>()
            .AddTransient<EmailVerification>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(CardPolicies.ChangeTreasury,
                p => p.RequireClaim(CardClaims.ChangeTreasury));
        });

        return services;
    }
}
