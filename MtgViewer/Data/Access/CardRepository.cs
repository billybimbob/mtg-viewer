using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.EntityFrameworkCore;

using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Data.Access;

public sealed class CardRepository : ICardRepository
{
    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly ParseTextFilter _parseTextFilter;
    private readonly PageSize _pageSize;

    public CardRepository(IDbContextFactory<CardDbContext> dbFactory, ParseTextFilter parseTextFilter, PageSize pageSize)
    {
        _dbFactory = dbFactory;
        _parseTextFilter = parseTextFilter;
        _pageSize = pageSize;
    }

    public async Task<SeekResponse<CardCopy>> GetCardsAsync(CollectionFilter collectionFilter, CancellationToken cancellation)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellation);

        var filter = _parseTextFilter.Parse(collectionFilter.Search);

        var cards = FilterCards(dbContext.Cards, filter, collectionFilter.Colors)
            .Select(c => new CardCopy
            {
                Id = c.Id,
                Name = c.Name,

                ManaCost = c.ManaCost,
                ManaValue = c.ManaValue,

                SetName = c.SetName,
                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Held = c.Holds.Sum(c => c.Copies)
            });

        var cardList = await OrderCopies(cards, collectionFilter.Order)
            .SeekBy(collectionFilter.Direction)
                .After(c => c.Id == collectionFilter.Seek)
                .Take(_pageSize.Current)
            .ToSeekListAsync(cancellation);

        return cardList.ToSeekResponse();
    }

    private static IQueryable<Card> FilterCards(IQueryable<Card> cards, TextFilter filter, Color color)
    {
        string? name = filter.Name?.ToUpperInvariant();
        string? text = filter.Text?.ToUpperInvariant();

        string[] types = filter.Types?.ToUpperInvariant().Split() ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            // keep eye on perf, postgres is slow here
            cards = cards
                .Where(c => c.Name.ToUpper().Contains(name));
        }

        if (filter.Mana is ManaFilter mana)
        {
            cards = cards.Where(mana.CreateFilter());
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            cards = cards
                .Where(c => c.Text != null
                    && c.Text.ToUpper().Contains(text));
        }

        foreach (string type in types)
        {
            cards = cards
                .Where(c => c.Type.ToUpper().Contains(type));
        }

        if (color is not Color.None)
        {
            cards = cards
                .Where(c => c.Color.HasFlag(color));
        }

        return cards;
    }

    private static IOrderedQueryable<CardCopy> OrderCopies(IQueryable<CardCopy> copies, string? orderBy)
    {
        return orderBy switch
        {
            nameof(Card.ManaCost) => copies
                .OrderByDescending(c => c.ManaValue)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.SetName) => copies
                .OrderBy(c => c.SetName)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.Id),

            nameof(Card.Rarity) => copies
                .OrderByDescending(c => c.Rarity)
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.Holds) => copies
                .OrderByDescending(c => c.Held) // keep eye on, query is a bit expensive
                    .ThenBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id),

            nameof(Card.Name) or _ => copies
                .OrderBy(c => c.Name)
                    .ThenBy(c => c.SetName)
                    .ThenBy(c => c.Id)
        };
    }
}
