using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgApiManager.Lib.Service;
using MTGViewer.Middleware;
using MTGViewer.Services;

namespace MTGViewer;


public class Startup
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public Startup(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }


    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddRazorPages()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve;
            })
            .AddMvcOptions(options =>
            {
                options.Filters.Add<OperationCancelledFilter>();
            })
            .AddCookieTempDataProvider(options =>
            {
                options.Cookie.IsEssential = false;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

        services
            .AddServerSideBlazor()
            .AddHubOptions(options =>
            {
                options.EnableDetailedErrors = _env.IsDevelopment();
                options.MaximumReceiveMessageSize = 64 * 1_024;
            });

        services.AddCardStorage(_config);
        services.AddSingleton<PageSizes>();

        services
            .AddSymbols(options => options
                .AddFormatter<CardText>(isDefault: true)
                .AddTranslator<ManaTranslator>(isDefault: true));

        services.AddSingleton<ParseTextFilter>();

        services
            .AddSingleton<IMtgServiceProvider, MtgServiceProvider>()
            .AddScoped<ICardService>(provider => provider
                .GetRequiredService<IMtgServiceProvider>()
                .GetCardService());

        services
            .AddScoped<IMTGQuery, MtgApiQuery>()
            .AddScoped<MtgApiFlipQuery>();

        services
            .Configure<SeedSettings>(_config.GetSection(nameof(SeedSettings)))
            .AddScoped<BulkOperations>()
            .AddScoped<FileCardStorage>()
            .AddScoped<LoadingProgress>();

        if (_env.IsDevelopment())
        {
            services.AddDatabaseDeveloperPageExceptionFilter();
        }

        if (!_env.IsProduction())
        {
            services.AddScoped<CardDataGenerator>();
        }
    }


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
        app.UseCors(); // using cors just to disable for blazor

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseMiddleware<ContentSecurityPolicy>();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();

            endpoints
                .MapBlazorHub()
                .WithMetadata(new DisableCorsAttribute());

            endpoints.MapFallbackToPage("/_Host");
        });
    }
}
