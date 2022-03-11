using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using CsvHelper;

using MTGViewer.Services.Internal;

namespace MTGViewer.Services;


public class FileCardStorage
{
    private readonly string _defaultFilename;
    private readonly BulkOperations _bulkOperations;
    private readonly LoadingProgress _loadProgress;

    public FileCardStorage(
        IOptions<SeedSettings> seedOptions,
        BulkOperations bulkOperations, 
        LoadingProgress loadProgress)
    {
        _defaultFilename = Path.ChangeExtension(seedOptions.Value.JsonPath, ".json");
        _bulkOperations = bulkOperations;
        _loadProgress = loadProgress;
    }


    public ValueTask<Stream> GetUserBackupAsync(string userId, CancellationToken cancel = default)
    {
        var stream = _bulkOperations.GetUserStream(userId);

        return SerializeAsync(stream, cancel);
    }


    public ValueTask<Stream> GetTreasuryBackupAsync(CancellationToken cancel = default)
    {
        var stream = _bulkOperations.GetTreasuryStream();

        return SerializeAsync(stream, cancel);
    }


    public ValueTask<Stream> GetDefaultBackupAsync(CancellationToken cancel = default)
    {
        var stream = _bulkOperations.GetDefaultStream();

        return SerializeAsync(stream, cancel);
    }


    private async ValueTask<Stream> SerializeAsync(CardStream stream, CancellationToken cancel)
    {
        var serializeOptions = new JsonSerializerOptions 
        {
            ReferenceHandler = ReferenceHandler.Preserve
        };

        // copy all data to memory, keep eye on
        // could possibly use temp file?

        var utf8Stream = new MemoryStream();

        var data = await CardData.FromStreamAsync(stream, cancel);

        await JsonSerializer.SerializeAsync(utf8Stream, data, serializeOptions, cancel);

        utf8Stream.Position = 0;

        return utf8Stream;
    }


    public async Task WriteBackupAsync(string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var writer = File.Create(path);

        var stream = _bulkOperations.GetSeedStream();

        var serializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        await JsonSerializer.SerializeAsync(writer, stream, serializeOptions, cancel);
    }


    public async Task JsonSeedAsync(string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var reader = File.OpenRead(path);

        var deserializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            PropertyNameCaseInsensitive = true 
        };

        var data = await JsonSerializer.DeserializeAsync<CardData>(reader, deserializeOptions, cancel);
        if (data is null)
        {
            throw new ArgumentException(nameof(path));
        }

        _loadProgress.AddProgress(10); // percent is a guess, TODO: more informed value

        await _bulkOperations.SeedAsync(data, cancel);
    }



    public async Task JsonAddAsync(Stream jsonStream, CancellationToken cancel = default)
    {
        var deserializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            PropertyNameCaseInsensitive = true
        };

        var data = await JsonSerializer.DeserializeAsync<CardData>(jsonStream, deserializeOptions, cancel);
        if (data is null)
        {
            throw new ArgumentException(nameof(jsonStream));
        }

        _loadProgress.AddProgress(10); // percent is a guess, TODO: more informed value

        await _bulkOperations.MergeAsync(data, cancel);
    }


    private sealed class CsvCard
    {
        public string Name { get; set; } = string.Empty;
        public string MultiverseID { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }


    public async Task CsvAddAsync(Stream csvStream, CancellationToken cancel = default)
    {
        using var readStream = new StreamReader(csvStream);
        using var csv = new CsvReader(readStream, CultureInfo.InvariantCulture);

        var csvAdditions = await CsvAdditionsAsync(csv, cancel);

        _loadProgress.AddProgress(10); // percent is a guess, TODO: more informed value

        await _bulkOperations.MergeAsync(csvAdditions, cancel);
    }


    private ValueTask<Dictionary<string, int>> CsvAdditionsAsync(
        CsvReader csv, 
        CancellationToken cancel)
    {
        return csv
            .GetRecordsAsync<CsvCard>(cancel)
            .Where(cc => cc.Quantity > 0)

            .GroupByAwaitWithCancellation(
                MultiverseIdAsync,
                async (multiverseId, ccs, cnl) => 
                    (multiverseId, quantity: await ccs.SumAsync(cc => cc.Quantity, cnl)))

            .ToDictionaryAsync(
                cc => cc.multiverseId, cc => cc.quantity, cancel);

        ValueTask<string> MultiverseIdAsync(CsvCard card, CancellationToken _)
        {
            return ValueTask.FromResult(card.MultiverseID);
        }
    }
}