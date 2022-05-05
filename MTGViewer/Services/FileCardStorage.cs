using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using CsvHelper;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Services.Infrastructure;

namespace MTGViewer.Services;

public class FileCardStorage
{
    private readonly LoadingProgress _loadProgress;
    private readonly MergeHandler _mergeHandler;

    public FileCardStorage(LoadingProgress loadProgress, MergeHandler mergeHandler)
    {
        _loadProgress = loadProgress;
        _mergeHandler = mergeHandler;
    }

    public async Task AddFromJsonAsync(Stream jsonStream, CancellationToken cancel = default)
    {
        var deserializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        var data = await JsonSerializer.DeserializeAsync<CardData>(jsonStream, deserializeOptions, cancel);

        if (data is null)
        {
            throw new ArgumentException("Json file format is not valid", nameof(jsonStream));
        }

        _loadProgress.AddProgress(10); // percent is a guess, TODO: more informed value

        await _mergeHandler.MergeAsync(data, cancel);
    }

    private sealed class CsvCard
    {
        public string Name { get; set; } = string.Empty;
        public string MultiverseID { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public async Task AddFromCsvAsync(Stream csvStream, CancellationToken cancel = default)
    {
        using var readStream = new StreamReader(csvStream);
        using var csv = new CsvReader(readStream, CultureInfo.InvariantCulture);

        var csvAdditions = await CsvAdditionsAsync(csv, cancel);

        _loadProgress.AddProgress(10); // percent is a guess, TODO: more informed value

        await _mergeHandler.MergeAsync(csvAdditions, cancel);
    }

    private static async Task<IReadOnlyDictionary<string, int>> CsvAdditionsAsync(
        CsvReader csv,
        CancellationToken cancel)
    {
        static ValueTask<string> MultiverseIdAsync(CsvCard card, CancellationToken _)
        {
            return ValueTask.FromResult(card.MultiverseID);
        }

        var additions = await csv
            .GetRecordsAsync<CsvCard>(CancellationToken.None) // cancel token given later
            .Where(cc => cc.Quantity > 0)

            .GroupByAwaitWithCancellation(
                MultiverseIdAsync,
                async (multiverseId, ccs, cnl) =>
                    (multiverseId, quantity: await ccs.SumAsync(cc => cc.Quantity, cnl)))

            .ToDictionaryAsync(
                cc => cc.multiverseId, cc => cc.quantity, cancel);

        // ensure that the iter order is deterministic

        return new SortedList<string, int>(additions);
    }
}
