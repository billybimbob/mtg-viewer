
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.EntityFrameworkCore;

using MtgViewer.Services.Search.Database;

namespace MtgViewer.Services.Search;

public sealed class MtgAllPrintings : IMtgQuery
{
    private const int MaxSize = 100;

    private readonly AllPrintingsDbContext _dbContext;
    private readonly int _pageSize;

    public MtgAllPrintings(AllPrintingsDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize.Default;
    }

    public bool HasFlip(string cardName)
    {
        const string faceSplit = "//";

        const StringComparison ordinal = StringComparison.Ordinal;

        return cardName.Contains(faceSplit, ordinal);
    }

    public IAsyncEnumerable<Data.Card> CollectionAsync(IEnumerable<string> multiverseIds, CancellationToken cancel)
    {
        return _dbContext.Cards
            .Where(c => c.CardIdentifier.MultiverseId != null && multiverseIds.Contains(c.CardIdentifier.MultiverseId))
            .Include(c => c.CardIdentifier)
            .Include(c => c.Set)
            .AsNoTracking()
            .AsAsyncEnumerable()
            .SelectAwait(c => GetDataCardAsync(c, cancel));
    }

    public async Task<OffsetList<Data.Card>> SearchAsync(IMtgSearch search, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(search);

        var cardQuery = ApplyFilters(search);

        int pageSize = search.PageSize is > 0 and <= MaxSize
            ? search.PageSize
            : _pageSize;

        var cards = await cardQuery
            .Skip(search.Page * pageSize)
            .Take(pageSize)
            .AsAsyncEnumerable()
            .SelectAwait(c => GetDataCardAsync(c, cancel))
            .ToListAsync(cancel);

        int totalCount = await cardQuery.CountAsync(cancel);

        var offset = new Offset(search.Page, totalCount, pageSize);

        return new OffsetList<Data.Card>(offset, cards);
    }

    public async Task<Data.Card?> FindAsync(string id, CancellationToken cancel)
    {
        var card = await _dbContext.Cards
            .Include(c => c.CardIdentifier)
            .Include(c => c.Set)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Uuid == id, cancel);

        if (card is null)
        {
            return null;
        }

        return await GetDataCardAsync(card, cancel);
    }

    private async ValueTask<Data.Card> GetDataCardAsync(Card card, CancellationToken cancel)
    {
    }

    private IQueryable<Card> ApplyFilters(IMtgSearch search)
    {
        var cards = _dbContext.Cards
            .Include(c => c.CardIdentifier)
            .Include(c => c.Set)
            .Where(c => c.CardIdentifier.MultiverseId != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search.Name))
        {
            string name = search.Name.ToUpperInvariant();

            cards = cards.Where(c => c.Name != null && c.Name.ToUpperInvariant().Contains(name));
        }

        if (!string.IsNullOrEmpty(search.SetName))
        {
            string setName = search.SetName.ToUpperInvariant();

            cards = cards.Where(c => c.Set.Name != null && c.Set.Name.ToUpperInvariant().Contains(setName));
        }

        if (search.ManaValue is int manaValue)
        {
            cards = cards.Where(c => c.ManaValue == manaValue);
        }

        if (search.Colors is not Data.Color.None)
        {
            var colors = Data.Symbol.Colors
                .Where(kv => search.Colors.HasFlag(kv.Key))
                .Select(kv => kv.Value);

            foreach (string? color in colors)
            {
                cards = cards.Where(c => c.Colors != null && c.Colors.Contains(color));
            }
        }

        if (!string.IsNullOrEmpty(search.Power))
        {
            string power = search.Power.ToUpperInvariant();

            cards = cards.Where(c => c.Power != null && c.Power.ToUpperInvariant().Contains(power));
        }

        if (!string.IsNullOrEmpty(search.Toughness))
        {
            string toughness = search.Toughness.ToUpperInvariant();

            cards = cards.Where(c => c.Toughness != null && c.Toughness.ToUpperInvariant().Contains(toughness));
        }

        if (!string.IsNullOrEmpty(search.Loyalty))
        {
            string loyalty = search.Loyalty.ToUpperInvariant();

            cards = cards.Where(c => c.Loyalty != null && c.Loyalty.ToUpperInvariant().Contains(loyalty));
        }

        if (search.Rarity is Data.Rarity rarity)
        {
            cards = cards.Where(c => c.Rarity == rarity.ToString());
        }

        if (search.Types is not null)
        {
            char[] separator = { ' ', ',' };

            const StringSplitOptions notWhiteSpace = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

            string[] types = search.Types
                .ToUpperInvariant()
                .Trim()
                .Split(separator, notWhiteSpace);

            foreach (string type in types)
            {
                cards = cards.Where(c => c.Types != null && c.Types.ToUpperInvariant().Contains(type));
            }
        }

        if (!string.IsNullOrEmpty(search.Artist))
        {
            string artist = search.Artist.ToUpperInvariant();

            cards = cards.Where(c => c.Artist != null && c.Artist.ToUpperInvariant().Contains(artist));
        }

        if (!string.IsNullOrEmpty(search.Text))
        {
            string text = search.Text.ToUpperInvariant();

            cards = cards.Where(c => c.Text != null && c.Text.ToUpperInvariant().Contains(text));
        }

        if (!string.IsNullOrEmpty(search.Flavor))
        {
            string flavor = search.Flavor.ToUpperInvariant();

            cards = cards.Where(c => c.FlavorText != null && c.FlavorText.ToUpperInvariant().Contains(flavor));
        }

        return cards;
    }
}
