using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Areas.Identity.Services;

using MtgViewer.Data;
using MtgViewer.Data.Configuration;
using MtgViewer.Services;

using MtgViewer.Services.Infrastructure;
using MtgViewer.Services.Search;
using MtgViewer.Services.Seed;
using MtgViewer.Services.Symbols;

using MtgViewer.Triggers;
using MtgViewer.Tests.Utils;

namespace MtgViewer.Tests;

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
        var databaseOptions = DatabaseOptions.FromConfiguration(config);

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

        _ = databaseOptions.Provider switch
        {
            DatabaseOptions.Sqlite =>
                services
                    .AddDbContext<UserDbContext>(TestFactory.SqliteInMemory)
                    .AddDbContextFactory<CardDbContext>(TestFactory.SqliteInMemory, ServiceLifetime.Scoped),

            DatabaseOptions.InMemory or _ =>
                services
                    .AddDbContext<UserDbContext>(TestFactory.InMemoryDatabase)
                    .AddDbContextFactory<CardDbContext>(TestFactory.InMemoryDatabase, ServiceLifetime.Scoped),
        };

        services
            .Configure<IdentityOptions>(config)
            .AddScoped(TestFactory.CardUserStore)
            .AddScoped(TestFactory.CardUserManager)
            .AddScoped(TestFactory.CardSignInManager);

        services
            .AddScoped<PlayerManager>()
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
            .AddMtgQueries()
            .Configure<CardResultOptions>(config.GetSection(nameof(CardResultOptions)))
            .AddSingleton<TestCardService>()
            .AddScoped<TestMtgApiQuery>();

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
