using System;
using System.Collections.Generic;
using System.Collections.Paging;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Data;

namespace MTGViewer.Services;

public class MtgApiQuery : IMTGQuery
{
    private static readonly IReadOnlyCollection<string> _multipleValues = 
        new HashSet<string>(new []
        {
            nameof(CardQuery.Colors),
            nameof(CardQuery.Supertypes),
            nameof(CardQuery.Types),
            nameof(CardQuery.Subtypes)
        });

    private readonly ICardService _service;
    private readonly FixedCache _cache;

    private readonly int _pageSize;
    private readonly ILogger<MtgApiQuery> _logger;

    private bool _empty;
    private int _page;

    public MtgApiQuery(
        ICardService service,
        FixedCache cache, 
        PageSizes pageSizes,
        ILogger<MtgApiQuery> logger)
    {
        _service = service;
        _cache = cache;

        _pageSize = pageSizes.Default;
        _logger = logger;

        _empty = true;
        _page = 0;
    }


    public int Limit => 100;


    public void Reset()
    {
        _service.Reset();

        _empty = true;
        _page = 0;
    }


    public IMTGQuery Where(Expression<Func<CardQuery, bool>> predicate)
    {
        if (predicate.Body is not BinaryExpression binary
            || binary.NodeType is not ExpressionType.Equal)
        {
            throw new NotSupportedException("Only equality expressions are supported");
        }

        if (binary.Left is MemberExpression leftMember)
        {
            QueryProperty(leftMember.Member.Name, ParsePropertyValue(binary.Right));
        }
        else if (binary.Right is MemberExpression rightMember)
        {
            QueryProperty(rightMember.Member.Name, ParsePropertyValue(binary.Left));
        }
        else
        {
            throw new NotSupportedException("Comparisons must be done on the Card Query");
        }

        return this;

        object ParsePropertyValue(Expression expression)
        {
            // boxes, keep eye on
            var objCast = Expression.Convert(expression, typeof(object));

            if (Expression
                .Lambda<Func<object?>>(objCast)
                .Compile()
                .Invoke() is not object value)
            {
                throw new ArgumentException(nameof(predicate));
            }

            return value;
        }
    }


    private void QueryProperty(string propertyName, object propertyValue)
    {
        const BindingFlags binds = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;

        if (typeof(CardQueryParameter).GetProperty(propertyName, binds) == null)
        {
            throw new ArgumentException(nameof(propertyName));
        }

        const string pageName = nameof(CardQueryParameter.Page);
        const string pageSizeName = nameof(CardQueryParameter.PageSize);

        switch ((propertyName, propertyValue))
        {
            case (pageName, int page) when page != default:
                // translate zero-based page index to one-based page from the api
                AddParameter(propertyName, page + 1);
                _page = page;
                break;

            case (pageSizeName, int pageSize):
                AddParameter(propertyName, pageSize);
                break;

            case (pageName, _):
            case (pageSizeName, _):
                break;

            case (_, _) when TryString(out var stringValue):
                AddParameter(propertyName, stringValue);
                break;

            default:
                throw new ArgumentException(nameof(propertyValue));
        }

        bool TryString(out string? stringValue)
        {
            bool isMultiple = _multipleValues.Contains(propertyName);
            return TryToString(propertyValue, isMultiple, out stringValue);
        }
    }


    private bool TryToString(object paramValue, bool multipleValues, out string? stringValue)
    {
        const StringSplitOptions noWhitespace = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

        stringValue = paramValue.ToString();

        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        // truncates any whitespace between logical operators

        stringValue = string.Join(
            IMTGQuery.Or,
            stringValue.Split(IMTGQuery.Or, noWhitespace));

        if (multipleValues)
        {
            stringValue = string.Join(
                IMTGQuery.And, 
                Regex.Split(stringValue, $@"(?:\s*{IMTGQuery.And}\s*)|\s+"));
        }

        return true;
    }


    private void AddParameter<TParameter>(string name, TParameter value)
    {
        var xParam = Expression.Parameter(typeof(CardQueryParameter), "x");
        var propExpr = Expression.Property(xParam, name);

        var expression = Expression.Lambda<Func<CardQueryParameter, TParameter>>(propExpr, xParam);

        _service.Where(expression, value);
        _empty = false;
    }


    /// <remarks> 
    /// This method is not thread safe, so multiple calls of this method cannot 
    /// be active at the same time. Be sure to <see langword="await"/> before 
    /// executing another search.
    /// </remarks>
    public async Task<OffsetList<Card>> SearchAsync(CancellationToken cancel = default)
    {
        if (_empty)
        {
            return OffsetList<Card>.Empty();
        }

        cancel.ThrowIfCancellationRequested();

        var response = await _service
            // .Where(c => c.OrderBy, "name") get error code 500 with this
            .AllAsync();

        cancel.ThrowIfCancellationRequested();

        var matches = LoggedUnwrap(response) ?? Enumerable.Empty<ICard>();

        if (!matches.Any())
        {
            _empty = true;
            _page = 0;

            return OffsetList<Card>.Empty();
        }

        var totalPages = response.PagingInfo.TotalPages;
        var pages = new Offset(_page, totalPages);

        var cards = matches
            .Select(c => c.ToCard())
            .Where(c => TestValid(c) is not null)
            .ToArray();

        // adventure cards have multiple entries with the same multiId

        foreach (var card in cards)
        {
            _cache[card.MultiverseId] = card;
        }

        _empty = true;
        _page = 0;

        return new OffsetList<Card>(pages, cards);
    }



    public async Task<Card?> FindAsync(string id, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (_cache.TryGetValue(id, out Card? card))
        {
            _logger.LogInformation($"using cached card for {id}");
            return card;
        }

        _logger.LogInformation($"refetching {id}");

        cancel.ThrowIfCancellationRequested();

        var result = await _service.FindAsync(id);

        cancel.ThrowIfCancellationRequested();

        var match = LoggedUnwrap(result);

        if (match is null)
        {
            _logger.LogError("match returned null");
            return null;
        }

        card = TestValid(match.ToCard());

        if (card is not null)
        {
            _cache[card.MultiverseId] = card;
        }

        return card;
    }


    private T? LoggedUnwrap<T>(IOperationResult<T> result) where T : class
    {
        var unwrap = result.Unwrap();

        if (unwrap is null)
        {
            _logger.LogError(result.Exception.ToString());
        }

        return unwrap;
    }


    private Card? TestValid(Card card)
    {
        if (!card.IsValid())
        {
            _logger.LogError($"{card?.Id} was found, but failed validation");
            return null;
        }
        else
        {
            return card;
        }
    }


    // public async Task<Data.Type[]> AllTypesAsync()
    // {
    //     var result = await _service.GetCardTypesAsync();
    //     var types = LoggedUnwrap(result) ?? Enumerable.Empty<string>();

    //     return types
    //         .Select(ty => new Data.Type(ty))
    //         .ToArray();
    // }


    // public async Task<Subtype[]> AllSubtypesAsync()
    // {
    //     var result = await _service.GetCardTypesAsync();
    //     var subtypes = LoggedUnwrap(result) ?? Enumerable.Empty<string>();

    //     return subtypes
    //         .Select(sb => new Subtype(sb))
    //         .ToArray();
    // }


    // public async Task<Supertype[]> AllSupertypesAsync()
    // {
    //     var result = await _service.GetCardTypesAsync();
    //     var supertypes = LoggedUnwrap(result) ?? Enumerable.Empty<string>();

    //     return supertypes
    //         .Select(sp => new Supertype(sp))
    //         .ToArray();
    // }
}


internal static class MtgApiExtension
{
    internal static TResult? Unwrap<TResult>(this IOperationResult<TResult> result)
        where TResult : class
    {    
        return result.IsSuccess ? result.Value : null;
    }


    internal static Card ToCard(this ICard card) => new Card
    {
        Id = card.Id,
        MultiverseId = card.MultiverseId,

        Name = card.Name,
        Names = (card.Names ?? Enumerable.Empty<string>())
            .Select(s => new Name(s, card.Id))
            .ToList(),

        Layout = card.Layout,

        Colors = (card.ColorIdentity ?? Enumerable.Empty<string>())
            .Select(id => Color.Symbols[id.ToUpper()]) 

            .Union(card.Colors ?? Enumerable.Empty<string>())
            .Select(s => new Color(s, card.Id))
            .ToList(),

        Types = (card.Types ?? Enumerable.Empty<string>())
            .Select(s => new Data.Type(s, card.Id))
            .ToList(),

        Subtypes = (card.SubTypes ?? Enumerable.Empty<string>())
            .Select(s => new Subtype(s, card.Id))
            .ToList(),

        Supertypes = (card.SuperTypes ?? Enumerable.Empty<string>())
            .Select(s => new Supertype(s, card.Id))
            .ToList(),

        ManaCost = card.ManaCost,
        Cmc = card.Cmc,

        Rarity = card.Rarity,
        SetName = card.SetName,
        Artist = card.Artist,

        Text = card.Text,
        Flavor = card.Flavor,

        Power = card.Power,
        Toughness = card.Toughness,
        Loyalty = card.Loyalty,
        ImageUrl = card.ImageUrl?.ToString()!
    };
}