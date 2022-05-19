using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Middleware;
using MTGViewer.Services;
using MTGViewer.Services.Infrastructure;
using MTGViewer.Services.Symbols;

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
                options.Filters.Add<ContentSecurityPolicyFilter>();
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

        services
            .AddSingleton<IActionContextAccessor, ActionContextAccessor>()
            .AddScoped<RouteDataAccessor>()
            .AddScoped<PageSize>();

        services
            .AddCardUsers(_config)
            .AddCardStorage(_config);

        services.Configure<MulliganOptions>(_config.GetSection(nameof(MulliganOptions)));

        services
            .AddSymbols(options => options
                .AddFormatter<CardText>(isDefault: true)
                .AddTranslator<ManaTranslator>(isDefault: true));

        services
            .AddSingleton<ParseTextFilter>()
            .AddScoped<LoadingProgress>()
            .AddScoped<FileCardStorage>();

        services
            .AddScoped<BackupFactory>()
            .AddScoped<MergeHandler>()
            .AddScoped<ResetHandler>();

        services.AddMtgQueries();

        if (_env.IsDevelopment())
        {
            services.AddDatabaseDeveloperPageExceptionFilter();
        }

        if (!_env.IsProduction())
        {
            services.AddCardSeedServices(_config);
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
