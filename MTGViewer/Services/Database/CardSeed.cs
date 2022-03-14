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

internal class CardSeed : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebHostEnvironment _env;

    public CardSeed(IServiceProvider serviceProvider, IWebHostEnvironment env)
    {
        _serviceProvider = serviceProvider;
        _env = env;
    }


    public async Task StartAsync(CancellationToken cancel)
    {
        if (_env.IsProduction())
        {
            return;
        }

        await using var scope = _serviceProvider.CreateAsyncScope();

        var scopeProvider = scope.ServiceProvider;

        var userContext = scopeProvider.GetRequiredService<UserDbContext>();
        var dbContext = scopeProvider.GetRequiredService<CardDbContext>();

        bool notEmpty = await dbContext.Cards.AnyAsync(cancel)
            || await userContext.Users.AnyAsync(cancel);

        if (notEmpty)
        {
            return;
        }

        var cardGen = scopeProvider.GetService<CardDataGenerator>();
        if (cardGen == null)
        {
            return;
        }

        await cardGen.GenerateAsync(cancel);
    }


    public Task StopAsync(CancellationToken cancel) => Task.CompletedTask;

}