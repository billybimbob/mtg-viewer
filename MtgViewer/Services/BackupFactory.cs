using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Services.Infrastructure;

namespace MtgViewer.Services;

public class BackupFactory
{
    private readonly CardDbContext _dbContext;

    public BackupFactory(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Stream> GetUserBackupAsync(string userId, CancellationToken cancel = default)
    {
        var stream = CardStream.User(_dbContext, userId);

        return await SerializeAsync(stream, cancel);
    }

    public async Task<Stream> GetTreasuryBackupAsync(CancellationToken cancel = default)
    {
        var stream = CardStream.Treasury(_dbContext);

        return await SerializeAsync(stream, cancel);
    }

    public async Task<Stream> GetDefaultBackupAsync(CancellationToken cancel = default)
    {
        var stream = CardStream.Default(_dbContext);

        return await SerializeAsync(stream, cancel);
    }

    private static async Task<Stream> SerializeAsync(CardStream stream, CancellationToken cancel)
    {
        var serializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve
        };

        // copy all data to memory, keep eye on
        // could possibly use temp file?

        var utf8Stream = new MemoryStream();

        var data = await CardData.FromAsync(stream, cancel);

        await JsonSerializer.SerializeAsync(utf8Stream, data, serializeOptions, cancel);

        utf8Stream.Position = 0;

        return utf8Stream;
    }

    public async Task<Stream> GetDeckExportAsync(int deckId, DeckMulligan mulligan, CancellationToken cancel = default)
    {

        var cards = mulligan == DeckMulligan.Theorycraft
            ? GetCardsTheorycraftAsync(deckId)
            : GetCardsBuiltAsync(deckId);

        var utf8Stream = new MemoryStream();
        var writer = new StreamWriter(utf8Stream);

        await foreach (var card in cards.WithCancellation(cancel))
        {
            writer.WriteLine("{0} {1}", card.Quantity, card.Name);
        }

        await writer.FlushAsync();

        utf8Stream.Position = 0;

        return utf8Stream;
    }

    private sealed class CardRow
    {
        public int Quantity { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private IAsyncEnumerable<CardRow> GetCardsBuiltAsync(int deckId)
    {
        return _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == deckId))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new CardRow
            {
                Name = c.Name,
                Quantity = c.Holds.Where(h => h.LocationId == deckId).Sum(h => h.Copies),
            })

            .AsAsyncEnumerable();
    }

    private IAsyncEnumerable<CardRow> GetCardsTheorycraftAsync(int deckId)
    {
        return _dbContext.Cards
            .Where(c => c.Holds.Any(h => h.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.Givebacks.Any(g => g.LocationId == deckId))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new CardRow
            {
                Name = c.Name,
                Quantity = c.Holds.Where(h => h.LocationId == deckId).Sum(h => h.Copies)
                    + c.Wants.Where(w => w.LocationId == deckId).Sum(w => w.Copies)
                    - c.Givebacks.Where(g => g.LocationId == deckId).Sum(g => g.Copies),
            })

            .AsAsyncEnumerable();
    }
}
