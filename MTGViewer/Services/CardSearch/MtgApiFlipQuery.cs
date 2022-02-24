using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;
using MTGViewer.Data;

namespace MTGViewer.Services;

public sealed class MtgApiFlipQuery
{
    private const string FaceSplit = "//";

    private readonly ICardService _cardService;
    private readonly ILogger<MtgApiFlipQuery> _logger;

    public MtgApiFlipQuery(ICardService cardService, ILogger<MtgApiFlipQuery> logger)
    {
        _cardService = cardService;
        _logger = logger;
    }


    public async ValueTask<Card?> GetCardAsync(
        IOperationResult<ICard> result,
        CancellationToken cancel)
    {
        if (LoggedUnwrap(result) is not ICard iCard
            || GetValidatedCard(iCard) is not Card card)
        {
            return null;
        }

        if (!card.Name.Contains(FaceSplit))
        {
            return card;
        }

        if (await GetFlipAsync(card, cancel) is not Flip flip)
        {
            return null;
        }

        card.Flip = flip;
        return card;
    }


    public async ValueTask<IReadOnlyList<Card>> GetCardsAsync(
        IOperationResult<List<ICard>> result, 
        CancellationToken cancel)
    {
        if (LoggedUnwrap(result)
            is not List<ICard> iCards || !iCards.Any())
        {
            return Array.Empty<Card>();
        }

        return await iCards
            .Where(c => c.MultiverseId is not null)
            .OrderBy(c => c.MultiverseId)

            .GroupBy(c => (c.Name, c.Set),
                (_, cards) => new Queue<ICard>(cards))

            .ToAsyncEnumerable()
            .SelectAwaitWithCancellation( CardWithFlipAsync )
            .OfType<Card>()
            .ToListAsync(cancel);
    }


    private async ValueTask<Card?> CardWithFlipAsync(Queue<ICard> cardGroup, CancellationToken cancel)
    {
        if (!cardGroup.TryDequeue(out var iCard)
            || GetValidatedCard(iCard) is not Card card)
        {
            return null;
        }

        if (!card.Name.Contains(FaceSplit))
        {
            return card;
        }

        if (cardGroup.TryDequeue(out var iFlip)
            && GetValidatedFlip(iFlip) is Flip groupFlip)
        {
            // assume closest multiverseId card is the flip
            // keep eye on

            card.Flip = groupFlip;
            return card;
        }

        // has to search for an individual card, which is very inefficient

        if (await GetFlipAsync(card, cancel) is not Flip flip)
        {
            return null;
        }

        card.Flip = flip;
        return card;
    }



    private async ValueTask<Flip?> GetFlipAsync(Card card, CancellationToken cancel)
    {
        var colors = Enum
            .GetValues<Color>()
            .Where(c => c is not Color.None && card.Color.HasFlag(c))
            .Select(c => Symbol.Colors[c]);

        var result = await _cardService
            .Where(c => c.ColorIdentity, string.Join(MtgApiQuery.And, colors))
            .Where(c => c.Name, card.Name)
            .Where(c => c.SetName, card.SetName)
            .Where(c => c.Layout, card.Layout)
            .AllAsync();

        cancel.ThrowIfCancellationRequested();

        var iFlip = LoggedUnwrap(result)
            ?.FirstOrDefault(c => c.Id != card.Id && c.MultiverseId is not null);

        if (iFlip is null)
        {
            return null;
        }

        return GetValidatedFlip(iFlip);
    }


    private T? LoggedUnwrap<T>(IOperationResult<T> result) where T : class
    {
        if (!result.IsSuccess)
        {
            _logger.LogError(result.Exception.ToString());
            return null;
        }

        return result.Value;
    }


    private Card? GetValidatedCard(ICard iCard)
    {
        if (!Enum.TryParse(iCard.Rarity, true, out Rarity rarity))
        {
            return null;
        }

        var card = new Card
        {
            Id = iCard.Id,
            MultiverseId = iCard.MultiverseId,
            Name = iCard.Name,

            Color = (iCard.ColorIdentity ?? Enumerable.Empty<string>())
                .Join(Symbol.Colors,
                    id => id, kv => kv.Value,
                    (_, kv) => kv.Key)
                .Aggregate(Color.None,
                    (color, iColor) => color | iColor),

            Layout = iCard.Layout,
            ManaCost = iCard.ManaCost,
            Cmc = iCard.Cmc,

            Type = iCard.Type,
            Rarity = rarity,
            SetName = iCard.SetName,

            Text = iCard.Text,
            Flavor = iCard.Flavor,

            Power = iCard.Power,
            Toughness = iCard.Toughness,
            Loyalty = iCard.Loyalty,

            Artist = iCard.Artist,
            ImageUrl = iCard.ImageUrl?.ToString()!
        };

        var nullCheck = new NullValidation<Card>(card);
        var validationContext = new ValidationContext(nullCheck);

        if (!Validator.TryValidateObject(nullCheck, validationContext, null))
        {
            _logger.LogError($"{card?.Id} was found, but failed validation");
            return null;
        }

        return card;
    }


    private Flip? GetValidatedFlip(ICard iFlip)
    {
        var flip = new Flip
        {
            MultiverseId = iFlip.MultiverseId,
            ManaCost = iFlip.ManaCost,
            Cmc = iFlip.Cmc,

            Type = iFlip.Type,
            Text = iFlip.Text,
            Flavor = iFlip.Flavor,

            Power = iFlip.Power,
            Toughness = iFlip.Toughness,
            Loyalty = iFlip.Loyalty,

            ImageUrl = iFlip.ImageUrl?.ToString()!,
            Artist = iFlip.Artist
        };

        var nullCheck = new NullValidation<Flip>(flip);
        var validationContext = new ValidationContext(nullCheck);

        if (!Validator.TryValidateObject(nullCheck, validationContext, null))
        {
            _logger.LogError($"{iFlip?.Id} was found, but failed validation");
            return null;
        }

        return flip;
    }
}
