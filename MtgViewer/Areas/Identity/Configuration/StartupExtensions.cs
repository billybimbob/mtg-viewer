using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;
using MtgViewer.Utils;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
{
    public static IServiceCollection AddCardUsers(this IServiceCollection services, IConfiguration config)
    {
        string connString = config.GetConnectionString("Users") ?? config.GetConnectionString("Cards");

        services.AddDbContext<UserDbContext>(options =>
        {
            _ = config.GetConnectionString("Provider") switch
            {
                "Postgresql" => options.UseNpgsql(connString.ToNpgsqlConnectionString()),
                "SqlServer" => options.UseSqlServer(connString),
                "Sqlite" or _ => options.UseSqlite(connString),
            };
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

        services
            .Configure<SenderOptions>(config.GetSection(nameof(SenderOptions)))
            .AddTransient<IEmailSender, EmailSender>()
            .AddTransient<EmailVerification>()
            .AddScoped<PlayerManager>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(CardPolicies.ChangeTreasury,
                p => p.RequireClaim(CardClaims.ChangeTreasury));
        });

        return services;
    }
}
