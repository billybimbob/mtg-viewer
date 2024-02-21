using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public sealed class MtgApiQuery : IMtgQuery
{
    public const char Or = '|';
    public const char And = ',';

    private const string RequiredAttributes = "multiverseId,imageUrl";
    private const int MaxSize = 100;

    private readonly ICardService _cardService;
    private readonly int _pageSize;

    private readonly LoadingProgress _loadProgress;
    private readonly ILogger<MtgApiQuery> _logger;

    public MtgApiQuery(
        ICardService cardService,
        PageSize pageSize,
        LoadingProgress loadProgress,
        ILogger<MtgApiQuery> logger)
    {
        _cardService = cardService;
        _pageSize = pageSize.Default;
        _loadProgress = loadProgress;
        _logger = logger;
    }

    public bool HasFlip(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName))
        {
            return false;
        }

        const string faceSplit = "//";

        const StringComparison ordinal = StringComparison.Ordinal;

        return cardName.Contains(faceSplit, ordinal);
    }

    public async Task<OffsetList<Card>> SearchAsync(IMtgSearch search, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(search);

        if (search.IsEmpty)
        {
            return OffsetList.Empty<Card>();
        }

        var response = await ApplySearch(search).AllAsync().WaitAsync(cancel);

        var items = await TranslateAsync(response, cancel);

        var offset = new Offset(search.Page, response.PagingInfo.TotalPages);

        return new OffsetList<Card>(offset, items);
    }

    private ICardService ApplySearch(IMtgSearch search)
    {
        var cards = _cardService;

        cards = WhereNotEmpty(cards, c => c.Name, search.Name);
        cards = WhereNotEmpty(cards, c => c.SetName, search.SetName);

        if (search.ManaValue is int manaValue)
        {
            var invariant = CultureInfo.InvariantCulture;

            cards = cards.Where(c => c.Cmc, manaValue.ToString(invariant));
        }

        if (search.Colors is not Color.None)
        {
            string colors = Symbol.Colors
                .Where(kv => search.Colors.HasFlag(kv.Key))
                .Select(kv => kv.Value)
                .Join(And);

            cards = cards.Where(c => c.ColorIdentity, colors);
        }

        cards = WhereNotEmpty(cards, c => c.Power, search.Power);
        cards = WhereNotEmpty(cards, c => c.Toughness, search.Toughness);
        cards = WhereNotEmpty(cards, c => c.Loyalty, search.Loyalty);

        if (search.Rarity is Rarity rarity)
        {
            cards = cards.Where(c => c.Rarity, rarity.ToString());
        }

        if (search.Types is not null)
        {
            char[] separator = { ' ', And };

            const StringSplitOptions notWhiteSpace
                = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

            string types = search.Types
                .Trim()
                .Split(separator, notWhiteSpace)
                .Join(And);

            cards = cards.Where(c => c.Type, types);
        }

        cards = WhereNotEmpty(cards, c => c.Artist, search.Artist);
        cards = WhereNotEmpty(cards, c => c.Text, search.Text);
        cards = WhereNotEmpty(cards, c => c.Flavor, search.Flavor);

        int pageSize = search.PageSize is > 0 and <= MaxSize
            ? search.PageSize
            : _pageSize;

        return cards
            .Where(c => c.Page, search.Page + 1)
            .Where(c => c.PageSize, pageSize)
            .Where(c => c.Contains, RequiredAttributes);
    }

    private static ICardService WhereNotEmpty(
        ICardService cards,
        Expression<Func<CardQueryParameter, string>> property,
        string? value)
    {
        return string.IsNullOrEmpty(value)
            ? cards
            : cards.Where(property, value);
    }

    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds, CancellationToken cancel = default)
    {
        multiverseIds ??= Enumerable.Empty<string>();

        return BulkSearchAsync(multiverseIds, cancel);
    }

    private async IAsyncEnumerable<Card> BulkSearchAsync(
        IEnumerable<string> multiverseIds,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        const int chunkSize = (int)(MaxSize * 0.9f); // leave wiggle room for result

        cancel.ThrowIfCancellationRequested();

        var chunks = multiverseIds
            .Distinct()
            .Chunk(chunkSize)
            .ToList();

        _loadProgress.Ticks += chunks.Count;

        foreach (string[] multiverseChunk in chunks)
        {
            if (!multiverseChunk.Any())
            {
                continue;
            }

            var response = await _cardService
                .Where(c => c.MultiverseId, multiverseChunk.Join(Or))
                .AllAsync()
                .WaitAsync(cancel);

            var validated = await TranslateAsync(response, cancel);

            foreach (var card in validated)
            {
                yield return card;
            }

            _loadProgress.AddProgress();
        }
    }

    public async Task<Card?> FindAsync(string id, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var result = await _cardService.FindAsync(id).WaitAsync(cancel);

        return await TranslateAsync(result, cancel);
    }

    #region Translate Results

    private async ValueTask<IReadOnlyList<Card>> TranslateAsync(
        IOperationResult<List<ICard>> result,
        CancellationToken cancel)
    {
        if (LoggedUnwrap(result) is not IReadOnlyList<ICard> iCards
            || iCards.Count is 0)
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
            .Except(missingMultiverseId)
            .OrderBy(c => c.MultiverseId)
            .GroupBy(c => (c.Name, c.Set))
            .ToAsyncEnumerable()
            .SelectMany(q => TranslateAsync(q))
            .ToListAsync(cancel);
    }

    private async IAsyncEnumerable<Card> TranslateAsync(
        IEnumerable<ICard> similarCards,
        [EnumeratorCancellation] CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        var similarCardsQueue = new Queue<ICard>(similarCards);

        while (similarCardsQueue.TryDequeue(out var iCard))
        {
            if (!HasFlip(iCard.Name))
            {
                if (Validate(iCard, null) is Card card)
                {
                    yield return card;
                }

                continue;
            }

            if (similarCardsQueue.TryDequeue(out var similarICard)
                && Validate(similarICard) is Flip similarFlip
                && Validate(iCard, similarFlip) is Card similarFlipCard)
            {
                // assume closest multiverseId card is the flip
                // keep eye on

                yield return similarFlipCard;
            }

            // has to search for an individual card, which is very inefficient

            var searchedICard = await SearchSimilarAsync(iCard, cancel);

            if (Validate(searchedICard) is Flip searchedFlip
                && Validate(iCard, searchedFlip) is Card searchedFlipCard)
            {
                yield return searchedFlipCard;
            }
        }
    }

    private async ValueTask<Card?> TranslateAsync(
        IOperationResult<ICard> result,
        CancellationToken cancel)
    {
        if (LoggedUnwrap(result) is not ICard iCard)
        {
            return null;
        }

        if (!HasFlip(iCard.Name))
        {
            return Validate(iCard, null);
        }

        var similar = await SearchSimilarAsync(iCard, cancel);
        var flip = Validate(similar);

        return Validate(iCard, flip);
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

    private async Task<ICard?> SearchSimilarAsync(ICard card, CancellationToken cancel)
    {
        string[] colors = card.ColorIdentity ?? Array.Empty<string>();

        var result = await _cardService
            .Where(c => c.ColorIdentity, colors.Join(And))

            .Where(c => c.Name, card.Name)
            .Where(c => c.Set, card.Set)
            .Where(c => c.Layout, card.Layout)

            .Where(c => c.PageSize, _pageSize)
            .Where(c => c.Contains, RequiredAttributes)
            .AllAsync()
            .WaitAsync(cancel);

        return LoggedUnwrap(result)
            ?.FirstOrDefault(c => c.Id != card.Id && c.MultiverseId is not null);
    }

    private Flip? Validate(ICard? iFlip)
    {
        if (iFlip is null)
        {
            return null;
        }

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

    private Card? Validate(ICard iCard, Flip? flip)
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

    #endregion
}
