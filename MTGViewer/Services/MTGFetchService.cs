using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MTGViewer.Data;

# nullable enable

namespace MTGViewer.Services;

public class MTGFetchService : IMtgQueryable<MTGFetchService, CardSearch>
{
    private static readonly IReadOnlySet<string> _multipleValues = 
        new HashSet<string>(new []
        {
            nameof(CardSearch.Colors),
            nameof(CardSearch.Supertypes),
            nameof(CardSearch.Types),
            nameof(CardSearch.Subtypes)
        });

    public const char Or = '|';
    public const char And = ',';


    private readonly ICardService _service;
    private readonly DataCacheService _cache;

    private readonly int _pageSize;
    private readonly ILogger<MTGFetchService> _logger;

    private bool _empty;

    public MTGFetchService(
        ICardService service,
        DataCacheService cache, 
        PageSizes pageSizes,
        ILogger<MTGFetchService> logger)
    {
        _service = service;
        _cache = cache;

        _pageSize = pageSizes.Default;
        _logger = logger;

        _empty = true;
    }


    public void Reset()
    {
        _service.Reset();
        _empty = true;
    }


    public MTGFetchService Where<TProperty>(
        Expression<Func<CardSearch, TProperty>> property, TProperty value)
    {
        if (property.Body is MemberExpression expression)
        {
            QueryProperty(expression.Member.Name, value);
        }

        return this;
    }



    private void QueryProperty(string propertyName, object? objValue)
    {
        var multipleValues = _multipleValues.Contains(propertyName);
        var paramValue = ToString(objValue, multipleValues);

        if (string.IsNullOrWhiteSpace(paramValue))
        {
            return;
        }

        const BindingFlags binds = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;

        if (typeof(CardQueryParameter).GetProperty(propertyName, binds) == null)
        {
            return;
        }

        var parameter = QueryParameter(propertyName);

        _service.Where(parameter, paramValue);
        _empty = false;
    }


    private static string ToString(object? paramValue, bool multipleValues)
    {
        const StringSplitOptions noWhitespace = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

        var strValue = paramValue?.ToString();

        if (string.IsNullOrWhiteSpace(strValue))
        {
            return string.Empty;
        }

        strValue = string.Join(Or, strValue.Split(Or, noWhitespace));

        if (multipleValues)
        {
            strValue = string.Join(And, Regex.Split(strValue, $@"(?:\s*{And}\s*)|\s+"));
        }

        return strValue;
    }


    private static Expression<Func<CardQueryParameter, string>> QueryParameter(string propName)
    {
        var xParam = Expression.Parameter(typeof(CardQueryParameter), "x");
        var propExpr = Expression.Property(xParam, propName);

        return Expression.Lambda<Func<CardQueryParameter, string>>(propExpr, xParam);
    }



    public async Task<PagedList<Card>> SearchAsync(int page = 0)
    {
        if (_empty)
        {
            return PagedList<Card>.Empty;
        }

        page = Math.Max(page, 0);

        var response = await _service
            .Where(c => c.PageSize, _pageSize)
            .Where(c => c.Page, page + 1)
            // .Where(c => c.OrderBy, "name") get error code 500 with this
            .AllAsync();

        _empty = true;

        var matches = LoggedUnwrap(response) ?? Enumerable.Empty<ICard>();

        if (!matches.Any())
        {
            return PagedList<Card>.Empty;
        }

        var pages = new Data.Pages(page, response.PagingInfo.TotalPages);

        var cards = matches
            .Select(c => c.ToCard())
            .Where(c => TestValid(c) is not null)
            .GroupBy(c => c.MultiverseId, (_, cards) => cards.First())
            .ToList();

        // adventure cards have multiple entries with the same multiId

        foreach (var card in cards)
        {
            _cache[card.MultiverseId] = card;
        }

        return new PagedList<Card>(pages, cards);
    }


    public Task<PagedList<Card>> MatchAsync(CardSearch search, int page = 0)
    {
        const BindingFlags binds = BindingFlags.Instance | BindingFlags.Public;

        foreach (var info in typeof(CardSearch).GetProperties(binds))
        {
            if (info?.GetGetMethod() is not null
                && info?.GetSetMethod() is not null)
            {
                QueryProperty(info.Name, info.GetValue(search));
            }
        }

        return SearchAsync(page);
    }


    public async Task<Card?> FindAsync(string id)
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

        var result = await _service.FindAsync(id);
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
        Id = card.Id, // id should be valid
        MultiverseId = card.MultiverseId,

        Name = card.Name,
        Names = (card.Names?.Select(s => new Name(s))
            ?? Enumerable.Empty<Name>())
            .ToList(),

        Layout = card.Layout,

        Colors = (card.Colors?.Select(s => new Color(s))
            ?? Enumerable.Empty<Color>())
            .ToList(),

        Types = (card.Types?.Select(s => new Data.Type(s))
            ?? Enumerable.Empty<Data.Type>())
            .ToList(),

        Subtypes = (card.SubTypes?.Select(s => new Subtype(s))
            ?? Enumerable.Empty<Subtype>())
            .ToList(),

        Supertypes = (card.SuperTypes?.Select(s => new Supertype(s))
            ?? Enumerable.Empty<Supertype>())
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
        ImageUrl = card.ImageUrl?.ToString()
    };
}