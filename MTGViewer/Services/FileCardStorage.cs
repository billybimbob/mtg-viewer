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
using Microsoft.Extensions.Configuration;
using CsvHelper;

using MTGViewer.Services.Internal;

namespace MTGViewer.Services;


public class FileCardStorage
{
    private readonly string _defaultFilename;
    private readonly BulkOperations _bulkOperations;

    public FileCardStorage(IConfiguration config, BulkOperations bulkOperations)
    {
        var filename = config.GetValue("JsonPath", "cards");

        _defaultFilename = Path.ChangeExtension(filename, ".json");

        _bulkOperations = bulkOperations;
    }


    public async Task<byte[]> GetBackupAsync(int? page = null, CancellationToken cancel = default)
    {
        var data = _bulkOperations.GetCardStream(DataScope.Paged, page);

        var serializeOptions = new JsonSerializerOptions 
        {
            ReferenceHandler = ReferenceHandler.Preserve
        };

        await using var utf8Stream = new MemoryStream(); // copy all data to memory, keep eye on

        await JsonSerializer.SerializeAsync(utf8Stream, data, serializeOptions, cancel);

        return utf8Stream.ToArray();
    }


    public async Task WriteBackupAsync(string? path = default, CancellationToken cancel = default)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), _defaultFilename);

        await using var writer = File.Create(path);

        var data = _bulkOperations.GetCardStream(DataScope.Full);

        var serializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        await JsonSerializer.SerializeAsync(writer, data, serializeOptions, cancel);
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