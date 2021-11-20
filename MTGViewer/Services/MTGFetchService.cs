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
    public const int Limit = 100;


    private readonly ICardService _service;
    private readonly DataCacheService _cache;

    private readonly int _pageSize;
    private readonly ILogger<MTGFetchService> _logger;

    private bool _empty;
    private int _page;

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
        _page = 0;
    }


    public void Reset()
    {
        _service.Reset();

        _empty = true;
        _page = 0;
    }


    public MTGFetchService Where<TParameter>(
        Expression<Func<CardSearch, TParameter>> property, TParameter value)
    {
        if (property.Body is MemberExpression expression)
        {
            QueryProperty(expression.Member.Name, value);
        }

        return this;
    }


    public MTGFetchService Where(CardSearch search)
    {
        if (search is null)
        {
            return this;
        }

        const BindingFlags binds = BindingFlags.Instance | BindingFlags.Public;

        foreach (var info in typeof(CardSearch).GetProperties(binds))
        {
            if (info.GetGetMethod() is not null
                && info.GetSetMethod() is not null)
            {
                QueryProperty(info.Name, info.GetValue(search));
            }
        }

        return this;
    }


    private void QueryProperty<TParameter>(string propertyName, TParameter parameter)
    {
        const BindingFlags binds = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;

        if (typeof(CardQueryParameter).GetProperty(propertyName, binds) == null)
        {
            return;
        }

        const string pageName = nameof(CardQueryParameter.Page);
        const string pageSizeName = nameof(CardQueryParameter.PageSize);

        bool TryString(out string? stringValue) =>
            TryToString(parameter, _multipleValues.Contains(propertyName), out stringValue);

        switch ((propertyName, parameter))
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
                break;
        }
    }


    private bool TryToString(object? paramValue, bool multipleValues, out string? stringValue)
    {
        const StringSplitOptions noWhitespace = StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries;

        stringValue = paramValue?.ToString();

        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        stringValue = string.Join(Or, stringValue.Split(Or, noWhitespace));

        if (multipleValues)
        {
            stringValue = string.Join(And, Regex.Split(stringValue, $@"(?:\s*{And}\s*)|\s+"));
        }

        return true;
    }


    private void AddParameter<TParameter>(string name, TParameter value)
    {
        var xParam = Expression.Parameter(typeof(CardQueryParameter), "x");
        var propExpr = Expression.Property(xParam, name);

        var expression = Expression.Lambda<Func<CardQueryParameter,TParameter>>(propExpr, xParam);

        _service.Where(expression, value);
        _empty = false;
    }


    /// <remarks> 
    /// This method is not thread safe, so multiple calls of this method cannot 
    /// be active at the same time. Be sure to <see langword="await"/> before 
    /// executing another search.
    /// </remarks>
    public async Task<PagedList<Card>> SearchAsync()
    {
        if (_empty)
        {
            return PagedList<Card>.Empty;
        }

        var response = await _service
            .Where(c => c.PageSize, _pageSize) // if pageSize set before, this is ignored
            // .Where(c => c.OrderBy, "name") get error code 500 with this
            .AllAsync();

        var totalPages = response.PagingInfo.TotalPages;

        var pages = new Data.Pages(_page, totalPages);

        _empty = true;
        _page = 0;

        var matches = LoggedUnwrap(response) ?? Enumerable.Empty<ICard>();

        if (!matches.Any())
        {
            return PagedList<Card>.Empty;
        }

        var cards = matches
            .Select(c => c.ToCard())
            .Where(c => TestValid(c) is not null)
            .ToArray();

        // adventure cards have multiple entries with the same multiId

        foreach (var card in cards)
        {
            _cache[card.MultiverseId] = card;
        }

        return new PagedList<Card>(pages, cards);
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
        Id = card.Id,
        MultiverseId = card.MultiverseId,

        Name = card.Name,
        Names = (card.Names ?? Enumerable.Empty<string>())
            .Select(s => new Name(s))
            .ToList(),

        Layout = card.Layout,

        Colors = (card.ColorIdentity ?? Enumerable.Empty<string>())
            .Select(id => Color.Symbols[id.ToUpper()]) 

            .Union(card.Colors ?? Enumerable.Empty<string>())
            .Select(s => new Color(s))
            .ToList(),

        Types = (card.Types ?? Enumerable.Empty<string>())
            .Select(s => new Data.Type(s))
            .ToList(),

        Subtypes = (card.SubTypes ?? Enumerable.Empty<string>())
            .Select(s => new Subtype(s))
            .ToList(),

        Supertypes = (card.SuperTypes ?? Enumerable.Empty<string>())
            .Select(s => new Supertype(s))
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