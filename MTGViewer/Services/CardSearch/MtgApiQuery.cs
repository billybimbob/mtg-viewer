using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;
using MTGViewer.Data;

namespace MTGViewer.Services;


public sealed class MtgApiQuery : IMTGQuery
{
    private const int Limit = 100;
    private const char Or = '|';
    private const char And = ',';

    public static readonly MethodInfo QueryMethod =
        typeof(MtgApiQuery)
            .GetMethod(
                nameof(MtgApiQuery.QueryProperty),
                BindingFlags.Instance | BindingFlags.NonPublic,
                new[] { 
                    typeof(IDictionary<,>).MakeGenericType(typeof(string), typeof(object)),
                    typeof(string),
                    typeof(object) })!;


    private readonly ICardService _service;
    private readonly FixedCache _cache;

    private readonly int _pageSize;
    private readonly LoadingProgress _loadProgress;
    private readonly ILogger<MtgApiQuery> _logger;

    private readonly PredicateConverter _predicateVisitor;

    public MtgApiQuery(
        ICardService service,
        FixedCache cache, 
        PageSizes pageSizes,
        LoadingProgress loadProgress,
        ILogger<MtgApiQuery> logger)
    {
        _service = service;
        _cache = cache;

        _pageSize = pageSizes.Default;
        _loadProgress = loadProgress;
        _predicateVisitor = new(this);

        _logger = logger;
    }



    public IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        var parameters = new Dictionary<string, object?>();

        QueryFromPredicate(parameters, predicate);

        return new MtgCardSearch(this, parameters);
    }


    internal IMTGCardSearch Where(
        MtgCardSearch values,
        Expression<Func<CardQuery, bool>> predicate)
    {
        var builder = values.ToBuilder();

        QueryFromPredicate(builder, predicate);

        return new MtgCardSearch(this, builder);
    }



    private void QueryFromPredicate(
        IDictionary<string, object?> parameters,
        Expression<Func<CardQuery, bool>> predicate)
    {
        if (_predicateVisitor.Visit(predicate) is MethodCallExpression call

            && (call.Object as ConstantExpression)?.Value == this
            && call.Method == QueryMethod
            && call.Arguments.ElementAtOrDefault(1) is ConstantExpression property
            && call.Arguments.ElementAtOrDefault(2) is ConstantExpression arg
            && property.Value is string propertyName)
        {
            QueryProperty(parameters, propertyName, arg.Value);
        }
        else
        {
            throw new NotSupportedException("Predicate cannot be parsed");
        }
    }

    private void QueryProperty(IDictionary<string, object?> parameters, string name, object? value)
    {
        const BindingFlags binds = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;

        if (typeof(CardQueryParameter).GetProperty(name, binds) == null)
        {
            throw new ArgumentException(nameof(name));
        }

        bool isMultiple = name is nameof(CardQuery.Colors) or nameof(CardQuery.Type);

        switch (value)
        {
            case IEnumerable<string> i:
                QueryProperty(parameters, name, i);
                break;

            case >0 when name != nameof(CardQuery.Cmc):
                parameters[name] = value;
                break;

            case 0 when name == nameof(CardQuery.Page):
                // translate zero-based page index to one-based page from the api
                parameters[name] = 1;
                break;

            case <0 when name != nameof(CardQuery.Cmc):
                break;

            case object when isMultiple
                && parameters.TryGetValue(name, out var values)
                && values is List<string> list
                && TryToString(value, isMultiple, out string s):

                list.AddRange(s.Split());
                break;

            case object when isMultiple && TryToString(value, isMultiple, out string s):
                parameters[name] = s.Split().ToList();
                break;

            case object when TryToString(value, isMultiple, out string s):
                parameters[name] = s;
                break;

            default:
                break;
        }
    }

    private void QueryProperty(
        IDictionary<string, object?> parameters, 
        string name, 
        IEnumerable values)
    {
        foreach (var v in values)
        {
            QueryProperty(parameters, name, v);
        }
    }


    private static bool TryToString(object paramValue, bool isMultiple, out string stringValue)
    {
        var toString = paramValue.ToString();

        if (string.IsNullOrWhiteSpace(toString))
        {
            stringValue = null!;
            return false;
        }
        
        // make sure that only one specific card is searched for

        toString = toString.Split(Or).FirstOrDefault();

        if (isMultiple && toString is not null)
        {
            toString = toString.Split(And).FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(toString))
        {
            stringValue = null!;
            return false;
        }

        stringValue = toString.Trim();
        return true;
    }


    /// <remarks> 
    /// This method is not thread safe, so multiple calls of this method cannot 
    /// be active at the same time. Be sure to <see langword="await"/> before 
    /// executing another search.
    /// </remarks>
    internal async ValueTask<OffsetList<Card>> SearchAsync(
        MtgCardSearch values,
        CancellationToken cancel = default)
    {
        if (values.IsEmpty())
        {
            return OffsetList<Card>.Empty();
        }

        cancel.ThrowIfCancellationRequested();

        int currentPage = values.Page;

        var response = await ApplyParameters(values)
            // .Where(c => c.OrderBy, "name") get error code 500 with this
            .Where(c => c.PageSize, _pageSize)
            .AllAsync();

        cancel.ThrowIfCancellationRequested();

        var totalPages = response.PagingInfo.TotalPages;
        var offset = new Offset(currentPage, totalPages);

        var matches = LoggedUnwrap(response) ?? Enumerable.Empty<ICard>();
        if (!matches.Any())
        {
            return OffsetList<Card>.Empty();
        }

        var cards = matches
            .Select( GetValidatedCard )
            .OfType<Card>()
            .ToArray();

        // adventure cards have multiple entries with the same multiId

        foreach (var card in cards)
        {
            _cache[card.MultiverseId] = card;
        }

        return new OffsetList<Card>(offset, cards);
    }


    private ICardService ApplyParameters(MtgCardSearch values)
    {
        foreach ((string name, object? value) in values.Parameters)
        {
            switch (value)
            {
                case <=0:
                    break;

                case >0 and int i:
                    AddParameter(name, i);
                    break;

                case IEnumerable<string> e:
                    AddParameter(name, string.Join(And, e));
                    break;

                case string s when !string.IsNullOrWhiteSpace(s):
                    AddParameter(name, s);
                    break;

                case null:
                default:
                    break;
            }
        }

        return _service;
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
    }


    
    public async ValueTask<IReadOnlyList<Card>> CollectionAsync(
        IEnumerable<string> multiverseIds,
        CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        var chunks = multiverseIds.Chunk(Limit).ToList();
        var cards = new List<Card>();

        _loadProgress.Ticks += chunks.Count;

        foreach (var multiChunk in chunks)
        {
            if (!multiChunk.Any())
            {
                continue;
            }

            var multiArg = string.Join(Or, multiChunk);

            var response = await _service
                .Where(c => c.MultiverseId, multiArg)
                .Where(c => c.PageSize, Limit)
                .AllAsync();

            cancel.ThrowIfCancellationRequested();

            var validated = (LoggedUnwrap(response) ?? Enumerable.Empty<ICard>())
                .Select( GetValidatedCard )
                .OfType<Card>()
                .ToArray();

            cards.AddRange(validated);
            
            _loadProgress.AddProgress();
        }

        return cards;
    }



    public async ValueTask<Card?> FindAsync(string id, CancellationToken cancel = default)
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
                .Select(s => new Name { Value = s, CardId = iCard.Id })
                .ToList(),

            Layout = iCard.Layout,

            Colors = (iCard.ColorIdentity ?? Enumerable.Empty<string>())
                .Select(id => Color.Symbols[id.ToUpper()]) 

                .Union(iCard.Colors ?? Enumerable.Empty<string>())
                .Select(s => new Color { Name = s, CardId = iCard.Id })
                .ToList(),

            Types = (iCard.Types ?? Enumerable.Empty<string>())
                .Select(s => new Data.Type { Name = s, CardId = iCard.Id })
                .ToList(),

            Subtypes = (iCard.SubTypes ?? Enumerable.Empty<string>())
                .Select(s => new Subtype { Name = s, CardId = iCard.Id })
                .ToList(),

            Supertypes = (iCard.SuperTypes ?? Enumerable.Empty<string>())
                .Select(s => new Supertype { Name = s, CardId = iCard.Id })
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