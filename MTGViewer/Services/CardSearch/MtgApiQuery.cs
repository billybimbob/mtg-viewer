using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using MtgApiManager.Lib.Service;
using MTGViewer.Data;

namespace MTGViewer.Services;


public sealed class MtgApiQuery : IMTGQuery
{
    internal const char Or = '|';
    internal const char And = ',';
    internal const int Limit = 100;

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
    private readonly MtgApiFlipQuery _flipQuery;

    private readonly int _pageSize;
    private readonly LoadingProgress _loadProgress;

    public MtgApiQuery(
        ICardService service,
        MtgApiFlipQuery flipQuery,
        PageSizes pageSizes,
        LoadingProgress loadProgress)
    {
        _cardService = service;
        _flipQuery = flipQuery;
        _pageSize = pageSizes.Default;
        _loadProgress = loadProgress;
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
            && call.Arguments[1] is ConstantExpression { Value: string propertyName }
            && call.Arguments[2] is ConstantExpression { Value: var value })
        {
            QueryProperty(parameters, propertyName, value);
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



    internal async ValueTask<OffsetList<Card>> SearchAsync(
        MtgCardSearch values,
        CancellationToken cancel)
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

        var cards = await _flipQuery.GetCardsAsync(response, cancel);

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



    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds)
    {
        return BulkSearchAsync(multiverseIds);
    }


    private async IAsyncEnumerable<Card> BulkSearchAsync(
        IEnumerable<string> multiverseIds,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();

        var chunks = multiverseIds
            .Distinct()
            .Chunk(Limit)
            .ToList();

        _loadProgress.Ticks += chunks.Count;

        foreach (var multiverseChunk in chunks)
        {
            if (!multiverseChunk.Any())
            {
                continue;
            }

            var multiverseArgs = string.Join(Or, multiverseChunk);

            var response = await _cardService
                .Where(c => c.MultiverseId, multiverseArgs)
                .Where(c => c.PageSize, Limit)
                .AllAsync();

            cancel.ThrowIfCancellationRequested();

            var validated = await _flipQuery.GetCardsAsync(response, cancel);

            foreach (var card in validated)
            {
                yield return card;
            }

            _loadProgress.AddProgress();
        }
    }



    public async ValueTask<Card?> FindAsync(string id, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        cancel.ThrowIfCancellationRequested();

        var result = await _cardService.FindAsync(id);

        cancel.ThrowIfCancellationRequested();

        return await _flipQuery.GetCardAsync(result, cancel);
    }

}