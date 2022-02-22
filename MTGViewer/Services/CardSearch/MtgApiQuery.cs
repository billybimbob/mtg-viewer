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
    internal const char Or = '|';
    internal const char And = ',';
    private const int Limit = 100;

    public static readonly MethodInfo QueryMethod =
        typeof(MtgApiQuery)
            .GetMethod(
                nameof(MtgApiQuery.QueryProperty),
                BindingFlags.Instance | BindingFlags.NonPublic,
                new[]
                { 
                    typeof(IDictionary<,>).MakeGenericType(typeof(string), typeof(IMtgParameter)),
                    typeof(string),
                    typeof(object)
                })!;

    private PredicateVisitor? _predicateConverter;
    private ExpressionVisitor PredicateConverter => _predicateConverter ??= new(this);

    private readonly ICardService _cardService;
    private readonly FixedCache _cache;

    private readonly int _pageSize;
    private readonly LoadingProgress _loadProgress;
    private readonly ILogger<MtgApiQuery> _logger;

    public MtgApiQuery(
        ICardService service,
        FixedCache cache, 
        PageSizes pageSizes,
        LoadingProgress loadProgress,
        ILogger<MtgApiQuery> logger)
    {
        _cardService = service;
        _cache = cache;

        _pageSize = pageSizes.Default;
        _loadProgress = loadProgress;
        _logger = logger;
    }



    public IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        var parameters = CardQueryParameters.Base
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return QueryFromPredicate(parameters, predicate);
    }


    internal IMTGCardSearch QueryFromPredicate(
        IDictionary<string, IMtgParameter> parameters,
        Expression<Func<CardQuery, bool>> predicate)
    {
        if (PredicateConverter.Visit(predicate) is MethodCallExpression call
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

        return new MtgCardSearch(this, parameters);
    }


    private void QueryProperty(IDictionary<string, IMtgParameter> parameters, string name, object? value)
    {
        if (!parameters.TryGetValue(name, out var parameter))
        {
            parameter = new MtgDefaultParameter(GetParameter(name));
        }

        parameters[name] = parameter.Accept(value);
    }


    private Expression<Func<CardQueryParameter, string>> GetParameter(string name)
    {
        const BindingFlags binds = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;

        var property = typeof(CardQueryParameter).GetProperty(name, binds);
        if (property == null || property.PropertyType != typeof(string))
        {
            throw new ArgumentException(nameof(name));
        }

        var param = Expression.Parameter(
            typeof(CardQueryParameter),
            typeof(CardQueryParameter).Name[0].ToString().ToLower());

        return Expression
            .Lambda<Func<CardQueryParameter, string>>(
                Expression.Property(param, name), param);
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
        if (values.IsEmpty)
        {
            return OffsetList<Card>.Empty;
        }

        cancel.ThrowIfCancellationRequested();

        int currentPage = values.Page;
        var response = await ApplyParameters(values).AllAsync();

        cancel.ThrowIfCancellationRequested();

        var totalPages = response.PagingInfo.TotalPages;
        var offset = new Offset(currentPage, totalPages);

        var matches = LoggedUnwrap(response) ?? Enumerable.Empty<ICard>();
        if (!matches.Any())
        {
            return OffsetList<Card>.Empty;
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
        foreach (var parameter in values.Parameters.Values)
        {
            parameter.Apply(_cardService);
        }

        // _cardService.Where(c => c.OrderBy, "name") get error code 500 with this

        if (values.Parameters.GetValueOrDefault(nameof(CardQuery.PageSize))
            ?.IsEmpty ?? true)
        {
            return _cardService.Where(c => c.PageSize, _pageSize);
        }

        return _cardService;
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

            var response = await _cardService
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

        var result = await _cardService.FindAsync(id);

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
        if (!Enum.TryParse(iCard.Rarity, true, out Rarity rarity))
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

            Color = (iCard.Colors ?? Enumerable.Empty<string>())
                .Select(c =>
                    Enum.TryParse(c, false, out Color color) ? color : default)
                .Aggregate(Color.None, (color, iColor) => color | iColor),

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

        if (!card.IsValid())
        {
            _logger.LogError($"{card?.Id} was found, but failed validation");
            return null;
        }

        return card;
    }
}