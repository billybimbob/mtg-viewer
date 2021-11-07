using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;
using MTGViewer.Services;


namespace MTGViewer
{
    public class Startup
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;

        public Startup(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddRazorPages();
            services.AddServerSideBlazor();

            services.AddSingleton<PageSizes>();

            services.AddSymbols(options => options
                .AddFormatter<CardText>(isDefault: true)
                .AddTranslator<ManaTranslator>(isDefault: true));

            services.AddCardStorage(_config);

            services.AddSingleton<DataCacheService>();
            services.AddSingleton<IMtgServiceProvider, MtgServiceProvider>();

            services.AddScoped<ICardService>(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

            services.AddScoped<MTGFetchService>();

            services.AddScoped<ITreasury, FlatVariableStorage>();
            services.AddScoped<JsonCardStorage>();

            if (_env.IsDevelopment())
            {
                services.AddScoped<CardDataGenerator>();
                services.AddDatabaseDeveloperPageExceptionFilter();
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            if (_env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapBlazorHub();
            });
        }
    }
}
