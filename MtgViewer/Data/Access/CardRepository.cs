using System;
using System.Collections.Generic;
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

    public async Task<IReadOnlyCollection<string>> GetExistingCardIdsAsync(IReadOnlyCollection<Card> cards, CancellationToken cancellation)
    {
        if (cards.Count is 0)
        {
            return Array.Empty<string>();
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellation);

        return await GetExistingCardIdsAsync(dbContext, cards, cancellation);
    }

    public async Task<IReadOnlyList<string>> GetShuffleOrderAsync(CancellationToken cancellation)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellation);

        return await dbContext.Cards
            .Select(c => c.Id)
            .OrderBy(_ => EF.Functions.Random())
            .Take(_pageSize.Current)
            .ToListAsync(cancellation);
    }

    public async Task<IReadOnlyList<CardImage>> GetCardImagesAsync(IReadOnlyList<string> cardIds, CancellationToken cancellation)
    {
        if (!cardIds.Any())
        {
            return Array.Empty<CardImage>();
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellation);

        var dbChunk = dbContext.Cards
            .Where(c => cardIds.Contains(c.Id))
            .Select(c => new CardImage
            {
                Id = c.Id,
                Name = c.Name,
                ImageUrl = c.ImageUrl
            })
            .AsAsyncEnumerable();

        // preserve order of chunk

        return await cardIds
            .ToAsyncEnumerable()
            .Join(dbChunk,
                cid => cid, c => c.Id,
                (_, preview) => preview)

            .ToListAsync(cancellation);
    }

    public async Task<SeekResponse<CardCopy>> GetCardCopiesAsync(CollectionFilter collectionFilter, CancellationToken cancellation)
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

    public async Task AddCardsAsync(IReadOnlyCollection<CardRequest> cardRequests, CancellationToken cancellation)
    {
        if (cardRequests.Count is 0)
        {
            return;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellation);

        var requestCards = cardRequests
            .Select(cr => cr.Card)
            .ToList();

        var existingIds = await GetExistingCardIdsAsync(dbContext, requestCards, cancellation);

        var existingCards = requestCards
            .IntersectBy(existingIds, c => c.Id);

        var newCards = requestCards
            .ExceptBy(existingIds, c => c.Id);

        dbContext.Cards.AttachRange(existingCards);
        dbContext.Cards.AddRange(newCards);

        await dbContext.AddCardsAsync(cardRequests, cancellation);

        await dbContext.SaveChangesAsync(cancellation);
    }

    private async Task<IReadOnlyCollection<string>> GetExistingCardIdsAsync(CardDbContext dbContext, IReadOnlyCollection<Card> cards, CancellationToken cancellation)
    {
        if (cards.Count > _pageSize.Limit)
        {
            var cardIds = cards
                .Select(c => c.Id)
                .ToAsyncEnumerable();

            return await dbContext.Cards
                .Select(c => c.Id)
                .AsAsyncEnumerable()
                .Intersect(cardIds)
                .ToListAsync(cancellation);
        }
        else
        {
            string[] cardIds = cards
                .Select(c => c.Id)
                .ToArray();

            return await dbContext.Cards
                .Select(c => c.Id)
                .Where(cid => cardIds.Contains(cid))
                .ToListAsync(cancellation);
        }
    }
}
