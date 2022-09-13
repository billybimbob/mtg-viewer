using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MtgViewer.Middleware;
using MtgViewer.Services;
using MtgViewer.Services.Infrastructure;
using MtgViewer.Services.Symbols;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

var config = builder.Configuration;
var env = builder.Environment;

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
        options.EnableDetailedErrors = env.IsDevelopment();
        options.MaximumReceiveMessageSize = 64 * 1_024;
    });

services
    .AddSingleton<IActionContextAccessor, ActionContextAccessor>()
    .AddScoped<RouteDataAccessor>()
    .AddScoped<PageSize>()
    .Configure<MulliganOptions>(config.GetSection(nameof(MulliganOptions)));

services
    .AddCardUsers(config)
    .AddCardStorage(config);

services
    .AddSymbols(options => options
        .AddFormatter<CardText>(isDefault: true)
        .AddTranslator<ManaTranslator>(isDefault: true));

services
    .AddScoped<MergeHandler>()
    .AddScoped<ResetHandler>()
    .AddScoped<BackupFactory>()
    .AddScoped<LoadingProgress>();

services
    .AddSingleton<ParseTextFilter>()
    .AddScoped<FileCardStorage>()
    .AddMtgQueries();

if (env.IsDevelopment())
{
    services.AddDatabaseDeveloperPageExceptionFilter();
}

if (!env.IsProduction())
{
    services.AddCardSeedServices(config);
}

await using var app = builder.Build();

if (env.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCors(); // using cors just to disable for blazor
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapBlazorHub().WithMetadata(new DisableCorsAttribute());
app.MapFallbackToPage("/_Host");

int? port = config.GetValue("PORT", null as int?);
string? url = port is null ? null : $"https://*:${port}";

await app.RunAsync(url);
