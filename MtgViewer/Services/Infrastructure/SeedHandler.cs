using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Services.Seed;

namespace MtgViewer.Services.Infrastructure;

public class SeedHandler
{
    private readonly SeedSettings _seedSettings;
    private readonly LoadingProgress _loadProgress;

    // treat each write function as a unit of work
    // reading data can reuse the same db context
    private readonly IDbContextFactory<CardDbContext> _dbFactory;

    private readonly UserManager<CardUser> _userManager;

    public SeedHandler(
        IOptions<SeedSettings> seedOptions,
        LoadingProgress loadProgress,
        IDbContextFactory<CardDbContext> dbFactory,
        UserManager<CardUser> userManager)
    {
        _seedSettings = seedOptions.Value;
        _loadProgress = loadProgress;
        _dbFactory = dbFactory;
        _userManager = userManager;
    }

    public async Task SeedAsync(CardData data, CancellationToken cancel = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        dbContext.Owners.AddRange(data.Owners);
        dbContext.Cards.AddRange(data.Cards);

        // ignore card ids, just assume it will be empty

        dbContext.Bins.AddRange(data.Bins);
        dbContext.Decks.AddRange(data.Decks);

        dbContext.Suggestions.AddRange(data.Suggestions);

        _loadProgress.Ticks = data.Users.Count + 1;

        await dbContext.SaveChangesAsync(cancel);

        _loadProgress.AddProgress();

        foreach (var user in data.Users)
        {
            await AddUserAsync(user, cancel);

            _loadProgress.AddProgress();
        }
    }

    private async ValueTask<IdentityResult> AddUserAsync(CardUser user, CancellationToken cancel)
    {
        var created = string.IsNullOrWhiteSpace(_seedSettings.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, _seedSettings.Password);

        cancel.ThrowIfCancellationRequested();

        var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);

        cancel.ThrowIfCancellationRequested();

        if (!providers.Any())
        {
            return created;
        }

        string token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        cancel.ThrowIfCancellationRequested();

        var confirmed = await _userManager.ConfirmEmailAsync(user, token);

        cancel.ThrowIfCancellationRequested();

        return confirmed;
    }

    public async Task SeedAsync(string? path = default, CancellationToken cancel = default)
    {
        string defaultFilename = Path.ChangeExtension(_seedSettings.FilePath, ".json");

        path ??= Path.Combine(Directory.GetCurrentDirectory(), defaultFilename);

        await using var reader = File.OpenRead(path);

        var deserializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        var data = await JsonSerializer.DeserializeAsync<CardData>(reader, deserializeOptions, cancel);

        if (data is null)
        {
            throw new ArgumentException("Json file format is not valid", nameof(path));
        }

        _loadProgress.AddProgress(10); // percent is a guess, TODO: more informed value

        await SeedAsync(data, cancel);
    }

    public async Task WriteBackupAsync(string? path = default, CancellationToken cancel = default)
    {
        string defaultFilename = Path.ChangeExtension(_seedSettings.FilePath, ".json");

        path ??= Path.Combine(Directory.GetCurrentDirectory(), defaultFilename);

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        await using var writer = File.Create(path);

        var stream = CardStream.All(dbContext, _userManager);

        var serializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        await JsonSerializer.SerializeAsync(writer, stream, serializeOptions, cancel);
    }
}
