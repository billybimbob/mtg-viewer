using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
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
            if (Expression
                .Lambda(expression)
                .Compile()
                .DynamicInvoke() is not object value)
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

            case (pageSizeName, int pageSize) when pageSize != default:
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
        var param = Expression.Parameter(
            typeof(CardQueryParameter),
            typeof(CardQueryParameter).Name[0].ToString().ToLower());

        var expression = Expression
            .Lambda<Func<CardQueryParameter, TParameter>>(
                Expression.Property(param, name),
                param);

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
            .Select( GetValidatedCard )
            .OfType<Card>()
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

        card = GetValidatedCard(match);

        if (card is not null)
        {
            _cache[card.MultiverseId] = card;
        }

        return card;
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
        if (!Enum.TryParse<Rarity>(iCard.Rarity, true, out var rarity))
        {
            return null;
        }

        var card = new Card
        {
            Id = iCard.Id,
            MultiverseId = iCard.MultiverseId,

            Name = iCard.Name,
            Names = (iCard.Names ?? Enumerable.Empty<string>())
                .Select(s => new Name(s, iCard.Id))
                .ToList(),

            Layout = iCard.Layout,

            Colors = (iCard.ColorIdentity ?? Enumerable.Empty<string>())
                .Select(id => Color.Symbols[id.ToUpper()]) 

                .Union(iCard.Colors ?? Enumerable.Empty<string>())
                .Select(s => new Color(s, iCard.Id))
                .ToList(),

            Types = (iCard.Types ?? Enumerable.Empty<string>())
                .Select(s => new Data.Type(s, iCard.Id))
                .ToList(),

            Subtypes = (iCard.SubTypes ?? Enumerable.Empty<string>())
                .Select(s => new Subtype(s, iCard.Id))
                .ToList(),

            Supertypes = (iCard.SuperTypes ?? Enumerable.Empty<string>())
                .Select(s => new Supertype(s, iCard.Id))
                .ToList(),

            ManaCost = iCard.ManaCost,
            Cmc = iCard.Cmc,

            Rarity = rarity,
            SetName = iCard.SetName,
            Artist = iCard.Artist,

            Text = iCard.Text,
            Flavor = iCard.Flavor,

            Power = iCard.Power,
            Toughness = iCard.Toughness,
            Loyalty = iCard.Loyalty,
            ImageUrl = iCard.ImageUrl?.ToString()!
        };

        if (!card.IsValid())
        {
            _logger.LogError($"{card?.Id} was found, but failed validation");
            return null;
        }

        return card;
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