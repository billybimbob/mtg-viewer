using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Areas.Identity.Services;

using MTGViewer.Data;
using MTGViewer.Data.Configuration;
using MTGViewer.Services;

using MTGViewer.Services.Infrastructure;
using MTGViewer.Services.Search;
using MTGViewer.Services.Seed;
using MTGViewer.Services.Symbols;

using MTGViewer.Triggers;
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
                config.AddUserSecrets<App>();
            });
    }

    public void ConfigureServices(IServiceCollection services, HostBuilderContext context)
    {
        var config = context.Configuration;
        var databaseOptions = DatabaseOptions.Bind(config);

        services
            .AddScoped<ActionContextAccessor>()
            .AddScoped<IActionContextAccessor>(services =>
                services.GetRequiredService<ActionContextAccessor>());

        services
            .AddRazorPageModels()
            .AddScoped<ActionHandlerFactory>()
            .AddScoped<RouteDataAccessor>()
            .AddScoped<PageSize>();

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
            .AddScoped(TestFactory.CardUserStore)
            .AddScoped(TestFactory.CardUserManager)
            .AddScoped(TestFactory.CardSignInManager);

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
            .AddScoped(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

        services
            .AddScoped<IMTGQuery, MtgApiQuery>()
            .AddScoped<MtgApiFlipQuery>();

        services
            .AddSingleton<ParseTextFilter>()
            .AddScoped<LoadingProgress>()
            .AddScoped<FileCardStorage>();

        services
            .AddScoped<BackupFactory>()
            .AddScoped<MergeHandler>()
            .AddScoped<ResetHandler>();

        services
            .Configure<SeedSettings>(config.GetSection(nameof(SeedSettings)))
            .AddScoped<SeedHandler>()
            .AddScoped<CardDataGenerator>()
            .AddScoped<TestDataGenerator>();

        services
            .AddTransient<IEmailSender, EmailSender>()
            .Configure<AuthMessageSenderOptions>(config);
    }
}
