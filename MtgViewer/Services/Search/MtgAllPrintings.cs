using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.EntityFrameworkCore;

using MtgViewer.Services.Search.Database;

namespace MtgViewer.Services.Search;

public sealed class MtgAllPrintings : IMtgQuery
{
    private const int MaxSize = 100;

    private readonly IDbContextFactory<AllPrintingsDbContext> _dbContextFactory;
    private readonly int _pageSize;

    public MtgAllPrintings(IDbContextFactory<AllPrintingsDbContext> dbContextFactory, PageSize pageSize)
    {
        _dbContextFactory = dbContextFactory;
        _pageSize = pageSize.Default;
    }

    public bool HasFlip(string cardName)
    {
        const string faceSplit = "//";

        const StringComparison ordinal = StringComparison.Ordinal;

        return cardName.Contains(faceSplit, ordinal);
    }

    public async IAsyncEnumerable<Data.Card> CollectionAsync(IEnumerable<string> multiverseIds, [EnumeratorCancellation] CancellationToken cancel)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancel);

        var cards = dbContext.Cards
            .Where(c => c.CardIdentifier.MultiverseId != null && multiverseIds.Contains(c.CardIdentifier.MultiverseId))
            .Include(c => c.CardIdentifier)
            .Include(c => c.Set)
            .AsNoTracking()
            .AsAsyncEnumerable()
            .WithCancellation(cancel);

        await foreach (var card in cards)
        {
            var dataCard = await GetDataCardAsync(dbContext, card, cancel);

            if (dataCard is not null)
            {
                yield return dataCard;
            }
        }
    }

    public async Task<OffsetList<Data.Card>> SearchAsync(IMtgSearch search, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(search);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancel);

        var cardQuery = ApplyFilters(dbContext, search);

        int pageSize = search.PageSize is > 0 and <= MaxSize
            ? search.PageSize
            : _pageSize;

        var cards = await cardQuery
            .Skip(search.Page * pageSize)
            .Take(pageSize)
            .AsAsyncEnumerable()
            .SelectAwait(c => GetDataCardAsync(dbContext, c, cancel))
            .OfType<Data.Card>()
            .ToListAsync(cancel);

        int totalCount = await cardQuery.CountAsync(cancel);

        var offset = new Offset(search.Page, totalCount, pageSize);

        return new OffsetList<Data.Card>(offset, cards);
    }

    public async Task<Data.Card?> FindAsync(string id, CancellationToken cancel)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancel);

        var card = await dbContext.Cards
            .Include(c => c.CardIdentifier)
            .Include(c => c.Set)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Uuid == id, cancel);

        if (card is null)
        {
            return null;
        }

        return await GetDataCardAsync(dbContext, card, cancel);
    }

    private static IQueryable<Card> ApplyFilters(AllPrintingsDbContext dbContext, IMtgSearch search)
    {
        var cards = dbContext.Cards
            .Include(c => c.CardIdentifier)
            .Include(c => c.Set)
            .Where(c => c.CardIdentifier.MultiverseId != null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search.Name))
        {
            string name = search.Name.ToUpperInvariant();

            cards = cards.Where(c => c.Name != null && c.Name.ToUpper().Contains(name));
        }

        if (!string.IsNullOrEmpty(search.SetName))
        {
            string setName = search.SetName.ToUpperInvariant();

            cards = cards.Where(c => c.Set.Name != null && c.Set.Name.ToUpper().Contains(setName));
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

            cards = cards.Where(c => c.Power != null && c.Power.ToUpper().Contains(power));
        }

        if (!string.IsNullOrEmpty(search.Toughness))
        {
            string toughness = search.Toughness.ToUpperInvariant();

            cards = cards.Where(c => c.Toughness != null && c.Toughness.ToUpper().Contains(toughness));
        }

        if (!string.IsNullOrEmpty(search.Loyalty))
        {
            string loyalty = search.Loyalty.ToUpperInvariant();

            cards = cards.Where(c => c.Loyalty != null && c.Loyalty.ToUpper().Contains(loyalty));
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
                cards = cards.Where(c => c.Types != null && c.Types.ToUpper().Contains(type));
            }
        }

        if (!string.IsNullOrEmpty(search.Artist))
        {
            string artist = search.Artist.ToUpperInvariant();

            cards = cards.Where(c => c.Artist != null && c.Artist.ToUpper().Contains(artist));
        }

        if (!string.IsNullOrEmpty(search.Text))
        {
            string text = search.Text.ToUpperInvariant();

            cards = cards.Where(c => c.Text != null && c.Text.ToUpper().Contains(text));
        }

        if (!string.IsNullOrEmpty(search.Flavor))
        {
            string flavor = search.Flavor.ToUpperInvariant();

            cards = cards.Where(c => c.FlavorText != null && c.FlavorText.ToUpper().Contains(flavor));
        }

        return cards;
    }

    private async ValueTask<Data.Card?> GetDataCardAsync(AllPrintingsDbContext dbContext, Card card, CancellationToken cancel)
    {
        if (card.Name is null)
        {
            return null;
        }

        if (!HasFlip(card.Name))
        {
            return Validate(card, null);
        }

        var similar = await SearchSimilarAsync(dbContext, card, cancel);
        var flip = Validate(similar);

        return Validate(card, flip);
    }

    private static Task<Card?> SearchSimilarAsync(AllPrintingsDbContext dbContext, Card card, CancellationToken cancel)
    {
        return dbContext.Cards
            .Where(c => c.CardIdentifier.MultiverseId != null)

            .Where(c => c.ColorIdentity == card.ColorIdentity)
            .Where(c => c.Name == card.Name)
            .Where(c => c.SetCode == card.SetCode)

            .Where(c => c.Layout == card.Layout)
            .Where(c => c.Uuid != card.Uuid)

            .FirstOrDefaultAsync(cancel);
    }

    private static Data.Flip? Validate(Card? flip)
    {
        if (flip is null)
        {
            return null;
        }

        string? multiverseId = flip.CardIdentifier.MultiverseId;

        if (multiverseId is null)
        {
            return null;
        }

        var dataFlip = new Data.Flip
        {
            MultiverseId = multiverseId,
            ManaCost = flip.ManaCost,
            ManaValue = flip.ManaValue,

            Type = flip.Types!,
            Text = flip.Text,
            Flavor = flip.FlavorText,

            Power = flip.Power,
            Toughness = flip.Toughness,
            Loyalty = flip.Loyalty,

            ImageUrl = GetImageUrl(multiverseId),
            Artist = flip.Artist!
        };

        var nullCheck = new NullValidation<Data.Flip>(dataFlip);
        var validationContext = new ValidationContext(nullCheck);

        if (!Validator.TryValidateObject(nullCheck, validationContext, null))
        {
            return null;
        }

        return dataFlip;
    }

    private Data.Card? Validate(Card card, Data.Flip? flip)
    {
        if (!Enum.TryParse(card.Rarity, true, out Data.Rarity rarity))
        {
            return null;
        }

        if (card.Name is null)
        {
            return null;
        }

        if (HasFlip(card.Name) && flip is null)
        {
            return null;
        }

        string? multiverseId = card.CardIdentifier.MultiverseId;

        if (multiverseId is null)
        {
            return null;
        }

        var dataCard = new Data.Card
        {
            Id = card.Uuid,
            MultiverseId = multiverseId,
            Name = card.Name,

            Color = (card.ColorIdentity?.Split(", ") ?? Enumerable.Empty<string>())
                .Join(Data.Symbol.Colors,
                    id => id, kv => kv.Value,
                    (_, kv) => kv.Key)
                .Aggregate(Data.Color.None,
                    (color, iColor) => color | iColor),

            Layout = card.Layout!,
            ManaCost = card.ManaCost,
            ManaValue = card.ManaValue,

            Type = card.Types!,
            Rarity = rarity,
            SetName = card.Set.Name!,
            Flip = flip,

            Text = card.Text,
            Flavor = card.FlavorText,

            Power = card.Power,
            Toughness = card.Toughness,
            Loyalty = card.Loyalty,

            Artist = card.Artist!,
            ImageUrl = GetImageUrl(multiverseId)
        };

        var nullCheck = new NullValidation<Data.Card>(dataCard);
        var validationContext = new ValidationContext(nullCheck);

        if (!Validator.TryValidateObject(nullCheck, validationContext, validationResults: null))
        {
            return null;
        }

        return dataCard;
    }

    private static string GetImageUrl(string multiverseId) => $"https://gatherer.wizards.com/Handlers/Image.ashx?multiverseid={multiverseId}&type=card";
}
