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
            services.AddScoped<EmptyProvider>();

            services.AddSingleton<DataCacheService>();
            services.AddSingleton<MtgServiceProvider>();
            services.AddScoped<MTGFetchService>();

            services.AddDbContext<CardDbContext>(TestFactory.EmptyDatabase);

            services.AddScoped<ISharedStorage, ExpandableSharedService>();
            services.AddScoped<JsonCardStorage>();

            services.AddScoped<CardDataGenerator>();
            services.AddScoped<TestDataGenerator>();

            services.AddDbContext<UserDbContext>(TestFactory.EmptyDatabase);
            services.AddScoped<UserManager<CardUser>>(TestFactory.CardUserManager);
        }
    }
}