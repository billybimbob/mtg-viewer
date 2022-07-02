using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public sealed class MtgApiFlipQuery
{
    private readonly ICardService _cardService;
    private readonly int _pageSize;
    private readonly ILogger<MtgApiFlipQuery> _logger;

    public MtgApiFlipQuery(
        ICardService cardService,
        PageSize pageSize,
        ILogger<MtgApiFlipQuery> logger)
    {
        _cardService = cardService;
        _pageSize = pageSize.Default;
        _logger = logger;
    }

    public bool HasFlip(string cardName)
    {
        const string faceSplit = "//";

        const StringComparison ordinal = StringComparison.Ordinal;

        return cardName.Contains(faceSplit, ordinal);
    }

    public async ValueTask<Card?> GetCardAsync(
        IOperationResult<ICard> result,
        CancellationToken cancel)
    {
        if (LoggedUnwrap(result) is not ICard iCard)
        {
            return null;
        }

        var flip = HasFlip(iCard.Name)
            ? await SearchFlipAsync(iCard, cancel)
            : null;

        return GetValidatedCard(iCard, flip);
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

        var missingMultiverseId = iCards
            .Where(c => c.MultiverseId is null);

        foreach (var missing in missingMultiverseId)
        {
            _logger.LogError("{Card} was found, but is missing multiverseId", missing.Name);
        }

        return await iCards
            .Where(c => c.MultiverseId is not null)
            .OrderBy(c => c.MultiverseId)

            .GroupBy(c => (c.Name, c.Set),
                (_, cards) => new Queue<ICard>(cards))

            .ToAsyncEnumerable()
            .SelectMany(q => GetValidatedCardsAsync(q))
            .ToListAsync(cancel);
    }

    private async IAsyncEnumerable<Card> GetValidatedCardsAsync(
        Queue<ICard> cardGroup,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        while (cardGroup.TryDequeue(out var iCard))
        {
            var flip = await FindFlipAsync(iCard, cardGroup, cancel);

            if (GetValidatedCard(iCard, flip) is Card card)
            {
                yield return card;
            }
        }
    }

    private async ValueTask<Flip?> FindFlipAsync(ICard card, Queue<ICard> cardGroup, CancellationToken cancel)
    {
        if (!HasFlip(card.Name))
        {
            return null;
        }

        if (cardGroup.TryDequeue(out var iFlip)
            && GetValidatedFlip(iFlip) is Flip groupFlip)
        {
            // assume closest multiverseId card is the flip
            // keep eye on

            return groupFlip;
        }

        // has to search for an individual card, which is very inefficient

        return await SearchFlipAsync(card, cancel);
    }

    private T? LoggedUnwrap<T>(IOperationResult<T> result) where T : class
    {
        if (!result.IsSuccess)
        {
            _logger.LogError("{Error}", result.Exception);
            return null;
        }

        return result.Value;
    }

    private async Task<Flip?> SearchFlipAsync(ICard card, CancellationToken cancel)
    {
        var colors = card.ColorIdentity ?? Enumerable.Empty<string>();

        var result = await _cardService
            .Where(c => c.ColorIdentity, string.Join(MtgApiQuery.And, colors))

            .Where(c => c.Name, card.Name)
            .Where(c => c.Set, card.Set)
            .Where(c => c.Layout, card.Layout)

            .Where(c => c.PageSize, _pageSize)
            .Where(c => c.Contains, MtgApiQuery.RequiredAttributes)
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

    private Card? GetValidatedCard(ICard iCard, Flip? flip)
    {
        if (!Enum.TryParse(iCard.Rarity, true, out Rarity rarity))
        {
            return null;
        }

        if (HasFlip(iCard.Name) && flip is null)
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
            ManaValue = iCard.Cmc,

            Type = iCard.Type,
            Rarity = rarity,
            SetName = iCard.SetName,
            Flip = flip,

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

        if (!Validator.TryValidateObject(nullCheck, validationContext, validationResults: null))
        {
            _logger.LogError("{Card} was found, but failed validation", card?.Id);
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
            ManaValue = iFlip.Cmc,

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
            _logger.LogError("{Flip} was found, but failed validation", iFlip?.Id);
            return null;
        }

        return flip;
    }
}
