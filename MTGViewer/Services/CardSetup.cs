using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Services;

internal class CardSetup : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebHostEnvironment _env;

    public CardSetup(IServiceProvider serviceProvider, IWebHostEnvironment env)
    {
        _serviceProvider = serviceProvider;
        _env = env;
    }


    public async Task StartAsync(CancellationToken cancel)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var scopeProvider = scope.ServiceProvider;

        // migrate all here so that no concurrent migrations occur

        var userContext = scopeProvider.GetRequiredService<UserDbContext>();
        await userContext.Database.MigrateAsync(cancel);

        var dbContext = scopeProvider.GetRequiredService<CardDbContext>();
        await dbContext.Database.MigrateAsync(cancel);

        if (_env.IsProduction())
        {
            return;
        }

        bool anyCards = await dbContext.Cards.AnyAsync(cancel);
        if (anyCards)
        {
            return;
        }

        if (_env.IsStaging())
        {
            await StagingSeedAsync(scopeProvider, cancel);
            return;
        }

        if (_env.IsDevelopment())
        {
            await DevelopmentSeedAsync(scopeProvider, cancel);
            return;
        }
    }


    private Task StagingSeedAsync(IServiceProvider provider, CancellationToken cancel)
    {
        var cardGen = provider.GetService<CardDataGenerator>();
        if (cardGen == null)
        {
            return Task.CompletedTask;
        }

        return cardGen.GenerateAsync(cancel);
    }


    private async Task DevelopmentSeedAsync(IServiceProvider provider, CancellationToken cancel)
    {
        var fileStorage = provider.GetRequiredService<FileCardStorage>();

        bool jsonSuccess = await fileStorage.TryJsonSeedAsync(cancel: cancel);
        if (jsonSuccess)
        {
            return;
        }

        var cardGen = provider.GetService<CardDataGenerator>();
        if (cardGen == null)
        {
            return;
        }

        await cardGen.GenerateAsync(cancel);
        await fileStorage.WriteJsonAsync(cancel: cancel);
    }


    public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;

}