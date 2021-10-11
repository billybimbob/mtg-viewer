using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

using MtgApiManager.Lib.Service;
using MTGViewer.Services;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddScoped<InMemoryConnection>();

            services.AddSingleton<PageSizes>();
            services.AddSingleton<IconMarkup>();

            // services.AddDbContext<CardDbContext>(TestFactory.SqliteInMemory);
            // services.AddDbContext<UserDbContext>(TestFactory.SqliteInMemory);

            services.AddDbContext<CardDbContext>(TestFactory.InMemoryDatabase);
            services.AddDbContext<UserDbContext>(TestFactory.InMemoryDatabase);

            services.AddScoped<UserManager<CardUser>>(TestFactory.CardUserManager);

            services.AddSingleton<DataCacheService>();
            services.AddSingleton<MtgServiceProvider>();
            services.AddScoped<MTGFetchService>();

            services.AddScoped<ISharedStorage, ExpandableSharedService>();
            services.AddScoped<JsonCardStorage>();

            services.AddScoped<CardDataGenerator>();
            services.AddScoped<TestDataGenerator>();
        }
    }
}