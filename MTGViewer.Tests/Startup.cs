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
using MTGViewer.Data.Triggers;
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
                config.AddUserSecrets<MTGViewer.App>();
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
            .AddTransient<ColorUpdate>()
            .AddTransient<ImmutableCard>()
            .AddTransient<StampUpdate>()
            .AddTransient<QuantityValidate>()
            .AddTransient<TradeValidate>();

        services
            .AddScoped<InMemoryConnection>()
            .AddScoped<TempFileName>();

        switch (databaseOptions.Provider)
        {
            case DatabaseOptions.Sqlite:
                services
                    .AddDbContextFactory<CardDbContext>(TestFactory.SqliteInMemory, ServiceLifetime.Scoped)
                    .AddDbContext<UserDbContext>(TestFactory.SqliteInMemory);
                break;

            case DatabaseOptions.InMemory:
            default:
                services
                    .AddDbContextFactory<CardDbContext>(TestFactory.InMemoryDatabase, ServiceLifetime.Scoped)
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
                .AddTranslator<ManaTranslator>())
            .AddSingleton<ParseTextFilter>();

        services
            .Configure<CardResultOptions>(config.GetSection(nameof(CardResultOptions)))
            .AddSingleton<TestCardService>()
            .AddScoped<TestMtgApiQuery>();

        services
            .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
            .AddScoped<ICardService>(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

        services
            .AddScoped<IMTGQuery, MtgApiQuery>()
            .AddScoped<MtgApiFlipQuery>();

        services
            .AddScoped<BulkOperations>()
            .AddScoped<FileCardStorage>()
            .AddScoped<LoadingProgress>();

        services
            .AddScoped<CardDataGenerator>()
            .AddScoped<TestDataGenerator>();

        services
            .AddTransient<IEmailSender, EmailSender>()
            .Configure<AuthMessageSenderOptions>(config);
    }
}
