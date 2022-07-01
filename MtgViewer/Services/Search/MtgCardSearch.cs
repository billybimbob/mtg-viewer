using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using MtgApiManager.Lib.Core;
using MtgApiManager.Lib.Model;
using MtgApiManager.Lib.Service;

using MtgViewer.Data;
using MtgViewer.Services.Search.Parameters;

namespace MtgViewer.Services.Search;

internal class MtgCardSearch : IMtgCardSearch
{
    private readonly ICardService _cardService;
    private readonly MtgApiFlipQuery _flipQuery;
    private readonly int _pageSize;

    private readonly IReadOnlyDictionary<string, IMtgParameter> _parameters;
    private readonly PredicateVisitor _predicateVisitor;

    private MtgCardSearch(
        ICardService cardService,
        MtgApiFlipQuery flipQuery,
        int pageSize,
        IReadOnlyDictionary<string, IMtgParameter> parameters)
    {
        _cardService = cardService;
        _flipQuery = flipQuery;
        _pageSize = pageSize;
        _parameters = parameters;
        _predicateVisitor = new PredicateVisitor(this, AddParameterMethod);
    }

    public MtgCardSearch(
        ICardService cardService,
        MtgApiFlipQuery flipQuery,
        PageSize pageSize)
        : this(cardService, flipQuery, pageSize.Default, BaseParameters)
    {
    }

    private static readonly MethodInfo AddParameterMethod
        = typeof(MtgCardSearch)
            .GetMethod(
                nameof(MtgCardSearch.AddParameter),
                BindingFlags.NonPublic | BindingFlags.Instance,
                new[] { typeof(string), typeof(object) })!;

    private static readonly IReadOnlyDictionary<string, IMtgParameter> BaseParameters
        = new Dictionary<string, IMtgParameter>
        {
            [nameof(CardQuery.Colors)] = new Parameters.Color(),
            [nameof(CardQuery.Rarity)] = new Parameters.Rarity(),
            [nameof(CardQuery.Type)] = new Types(),
            [nameof(CardQuery.Page)] = new Page(),
            [nameof(CardQuery.PageSize)] = new Parameters.PageSize()
        };

    private bool IsEmpty => _parameters.Values.All(p => p.IsEmpty);

    private int Page
    {
        get
        {
            const string queryPage = nameof(CardQuery.Page);

            var pageParameter = _parameters.GetValueOrDefault(queryPage);

            return (pageParameter as Page)?.Value ?? 0;
        }
    }

    private bool HasNoPageSize
    {
        get
        {
            const string pageSize = nameof(CardQuery.PageSize);

            return _parameters.GetValueOrDefault(pageSize)?.IsEmpty ?? true;
        }
    }

    public IMtgCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        if (_predicateVisitor.Visit(predicate)
            is Expression<Func<IMtgCardSearch>> addParameter)
        {
            return addParameter.Compile().Invoke();
        }

        throw new NotSupportedException("Predicate cannot be parsed");
    }

    private IMtgCardSearch AddParameter(string name, object? value)
    {
        var parameter = GetMtgParameter(name).Accept(value);

        if (_parameters.Contains(KeyValuePair.Create(name, parameter)))
        {
            return this;
        }

        var added = new Dictionary<string, IMtgParameter>(_parameters)
        {
            [name] = parameter
        };

        return new MtgCardSearch(_cardService, _flipQuery, _pageSize, added);
    }

    private IMtgParameter GetMtgParameter(string name)
    {
        if (_parameters.TryGetValue(name, out var parameter))
        {
            return parameter;
        }

        const BindingFlags binds = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        var queryProperty = typeof(CardQueryParameter).GetProperty(name, binds);

        if (queryProperty?.PropertyType != typeof(string))
        {
            throw new ArgumentException(
                "The parameter property does not exist or is not a string type", nameof(name));
        }

        var queryParameter = Expression.Parameter(
            typeof(CardQueryParameter),
            nameof(CardQueryParameter)[0].ToString().ToLowerInvariant());

        var nameProperty = Expression
            .Lambda<Func<CardQueryParameter, string>>(
                Expression.Property(queryParameter, queryProperty), queryParameter);

        return new Default(nameProperty);
    }

    public async Task<OffsetList<Card>> SearchAsync(CancellationToken cancel = default)
    {
        if (IsEmpty)
        {
            return OffsetList.Empty<Card>();
        }

        var response = await GetResponseAsync(cancel);

        var cards = await _flipQuery.GetCardsAsync(response, cancel);

        var offset = new Offset(Page, response.PagingInfo.TotalPages);

        return new OffsetList<Card>(offset, cards);
    }

    private async Task<IOperationResult<List<ICard>>> GetResponseAsync(CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        var cards = _cardService.Where(c => c.Contains, MtgApiQuery.RequiredAttributes);

        foreach (var parameter in _parameters.Values)
        {
            cards = parameter.Apply(cards);
        }

        // cards = cards.Where(c => c.OrderBy, "name") get error code 500 with this

        if (HasNoPageSize)
        {
            cards = cards.Where(c => c.PageSize, _pageSize);
        }

        var response = await cards.AllAsync();

        cancel.ThrowIfCancellationRequested();

        return response;
    }
}
