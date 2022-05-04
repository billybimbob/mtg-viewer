using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;
using Moq;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Services.Search;
using MTGViewer.Tests.Utils.Dto;

namespace MTGViewer.Tests.Utils;

public class CardResultOptions
{
    public string? JsonPath { get; set; }
}

public class TestCardService : ICardService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private readonly List<ICard> _cards;
    private readonly CardResultOptions _options;

    private bool _isLoaded;
    private IEnumerable<ICard> _query;

    private int _pageSize;
    private int _page;

    public TestCardService(IOptions<CardResultOptions> cardOptions)
    {
        _cards = new List<ICard>();
        _options = cardOptions.Value;
        _query = _cards.AsEnumerable();
    }

    private IAsyncEnumerable<ICard>? _asyncCards;
    public IAsyncEnumerable<ICard> Cards => _asyncCards ??= GetCardsAsync();

    public void Reset()
    {
        _query = _cards.AsEnumerable();
        _page = 0;
    }

    public ICardService Where<T>(Expression<Func<CardQueryParameter, T>> property, T value)
        where T : notnull
    {
        if (property.Body is not MemberExpression { Member.Name: string name })
        {
            throw new ArgumentException("Cannot query by given parameter", nameof(property));
        }

        if (name == nameof(CardQueryParameter.PageSize) && value is int size)
        {
            _pageSize = size;
            return this;
        }

        if (name == nameof(CardQueryParameter.Page) && value is int page)
        {
            _page = page;
            return this;
        }

        if (name == nameof(CardQueryParameter.Contains))
        {
            return this;
        }

        _query = _query
            .Where(c => QueryByPredicate(c, name, value));

        return this;
    }

    private static bool QueryByPredicate(ICard card, string propertyName, object? value)
    {
        object? cardValue = typeof(ICard)
            .GetProperty(propertyName)
            ?.GetValue(card);

        if (cardValue is IEnumerable<string> i)
        {
            cardValue = string.Join(MtgApiQuery.And, i);
        }

        if (value is not string s || cardValue is not string cardString)
        {
            return cardValue == value;
        }

        const StringComparison ordinal = StringComparison.Ordinal;

        string[] andValues = s.Split(MtgApiQuery.And);

        if (andValues.Length > 1)
        {
            return andValues
                .Aggregate(false, (b, v) => b && cardString.Equals(v, ordinal));
        }

        string[] orValues = s.Split(MtgApiQuery.Or);

        if (orValues.Length > 1)
        {
            return orValues
                .Aggregate(false, (b, v) => b || cardString.Equals(v, ordinal));
        }

        return cardString.Equals(s, ordinal);
    }

    public async Task<IOperationResult<List<ICard>>> AllAsync()
    {
        await AddCardResultsAsync(default);

        var cards = _query
            .Skip(_page * _pageSize)
            .Take(_pageSize)
            .ToList();

        var result = Result(cards, _cards.Count, _pageSize);

        Reset();

        return result;
    }

    public async Task<IOperationResult<ICard>> FindAsync(string id)
    {
        const StringComparison ordinal = StringComparison.Ordinal;

        await AddCardResultsAsync(default);

        var card = _cards
            .FirstOrDefault(c =>
                string.Equals(c.Id, id, ordinal)
                    || string.Equals(c.MultiverseId, id, ordinal));

        return card is null ? NotFound<ICard>() : Result(card);
    }

    public async Task<IOperationResult<ICard>> FindAsync(int multiverseId)
    {
        var invariant = CultureInfo.InvariantCulture;

        await AddCardResultsAsync(default);

        var card = _cards
            .FirstOrDefault(c => string
                .Equals(c.MultiverseId, multiverseId.ToString(invariant), StringComparison.Ordinal));

        return card is null ? NotFound<ICard>() : Result(card);
    }

    private async IAsyncEnumerable<ICard> GetCardsAsync([EnumeratorCancellation] CancellationToken cancel = default)
    {
        await AddCardResultsAsync(cancel);

        // convert to IAsyncEnemerable to allow access to linq functions

        foreach (var card in _cards)
        {
            yield return card;
        }
    }

    private async ValueTask AddCardResultsAsync(CancellationToken cancel)
    {
        if (_isLoaded)
        {
            return;
        }

        await _lock.WaitAsync(cancel);

        try
        {
            if (_options.JsonPath is not string path)
            {
                _isLoaded = true;
                return;
            }

            await using var cardData = File.OpenRead(path);

            var cards = JsonSerializer.DeserializeAsyncEnumerable<CardDto>(cardData, cancellationToken: cancel);

            await foreach (var card in cards)
            {
                if (card is not null)
                {
                    _cards.Add(new ApiCard(card));
                }
            }

            _isLoaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IOperationResult<List<string>>> GetFormatsAsync()
        => Task.FromResult(NotImplemented<List<string>>());

    public Task<IOperationResult<List<string>>> GetCardSubTypesAsync()
        => Task.FromResult(NotImplemented<List<string>>());

    public Task<IOperationResult<List<string>>> GetCardTypesAsync()
        => Task.FromResult(NotImplemented<List<string>>());

    public Task<IOperationResult<List<string>>> GetCardSuperTypesAsync()
        => Task.FromResult(NotImplemented<List<string>>());

    private static IOperationResult<T> NotImplemented<T>() where T : class
    {
        var result = new Mock<IOperationResult<T>>();

        _ = result
            .SetupGet(o => o.IsSuccess)
            .Returns(false);

        _ = result
            .SetupGet(o => o.Exception)
            .Returns(new NotImplementedException());

        _ = result
            .Setup(o => o.PagingInfo)
            .Returns(PagingInfo.Create(0, 0));

        return result.Object;
    }

    private static IOperationResult<T> NotFound<T>() where T : class
    {
        var result = new Mock<IOperationResult<T>>();

        result
            .SetupGet(o => o.IsSuccess)
            .Returns(false);

        result
            .SetupGet(o => o.Exception)
            .Returns(new MtgApiException("Failed to find value"));

        result
            .Setup(o => o.PagingInfo)
            .Returns(PagingInfo.Create(0, 0));

        return result.Object;
    }

    private static IOperationResult<T> Result<T>(T value) where T : class
    {
        var result = new Mock<IOperationResult<T>>();

        result
            .SetupGet(o => o.IsSuccess)
            .Returns(true);

        result
            .Setup(o => o.PagingInfo)
            .Returns(PagingInfo.Create(0, 0));

        result
            .SetupGet(o => o.Value)
            .Returns(value);

        return result.Object;
    }

    private static IOperationResult<T> Result<T>(T value, int total, int pageSize)
        where T : class
    {
        var result = new Mock<IOperationResult<T>>();

        result
            .SetupGet(o => o.IsSuccess)
            .Returns(true);

        result
            .Setup(o => o.PagingInfo)
            .Returns(PagingInfo.Create(total, pageSize));

        result
            .SetupGet(o => o.Value)
            .Returns(value);

        return result.Object;
    }
}
