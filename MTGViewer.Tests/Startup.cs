using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;
using MTGViewer.Data;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;

namespace MTGViewer.Tests;


public class Startup
{
    public void ConfigureHost(IHostBuilder hostBuilder)
    {
        hostBuilder
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: true);
                config.AddUserSecrets<MTGViewer.Program>();
            });
    }


    public void ConfigureServices(IServiceCollection services, HostBuilderContext context)
    {
        var config = context.Configuration;
        var databaseOptions = DatabaseOptions.Bind(config);
        
        services
            .AddRazorPageModels()
            .AddScoped<PageContextFactory>()
            .AddSingleton<PageSizes>();

        services
            .AddScoped<InMemoryConnection>()
            .AddScoped<TempFileName>();

        switch (databaseOptions.Provider)
        {
            case DatabaseOptions.Sqlite:
                services
                    .AddDbContext<CardDbContext>(TestFactory.SqliteInMemory)
                    .AddDbContext<UserDbContext>(TestFactory.SqliteInMemory);
                break;
            
            case DatabaseOptions.InMemory:
            default:
                services
                    .AddDbContext<CardDbContext>(TestFactory.InMemoryDatabase)
                    .AddDbContext<UserDbContext>(TestFactory.InMemoryDatabase);
                break;
        }

        services
            .Configure<IdentityOptions>(config)
            .AddScoped<UserStore<CardUser>>(TestFactory.CardUserStore)
            .AddScoped<UserManager<CardUser>>(TestFactory.CardUserManager)
            .AddScoped<SignInManager<CardUser>>(TestFactory.CardSignInManager);

        services
            .AddScoped<ReferenceManager>()
            .AddScoped<IUserClaimsPrincipalFactory<CardUser>, CardUserClaimsPrincipalFactory>()
            .AddAuthorization(options =>
            {
                options.AddPolicy(CardPolicies.ChangeTreasury,
                    p => p.RequireClaim(CardClaims.ChangeTreasury));
            });

        services
            .AddSymbols(options => options
                .AddFormatter<CardText>()
                .AddTranslator<ManaTranslator>());

        services
            .AddSingleton<FixedCache>()
            .AddMemoryCache(options =>
                // should auto evict from limit
                options.SizeLimit = config.GetValue("CacheLimit", 100L));

        services
            .AddScoped<IMTGQuery, MtgApiQuery>()
            .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
            .AddScoped<ICardService>(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

        services.AddScoped<BulkOperations>();
        services.AddScoped<FileCardStorage>();
        services.AddScoped<LoadingProgress>();

        services
            .AddScoped<CardDataGenerator>()
            .AddScoped<TestDataGenerator>();

        services
            .AddTransient<IEmailSender, EmailSender>()
            .Configure<AuthMessageSenderOptions>(config);
    }
}
