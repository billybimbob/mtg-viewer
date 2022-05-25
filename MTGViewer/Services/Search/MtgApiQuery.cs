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

namespace MTGViewer.Services.Search;

public sealed class MtgApiQuery : IMTGQuery
{
    public const char Or = '|';
    public const char And = ',';

    internal const int Limit = 100;
    internal const string RequiredAttributes = "multiverseId,imageUrl";

    public static readonly MethodInfo QueryMethod =
        typeof(MtgApiQuery)
            .GetMethod(
                nameof(MtgApiQuery.QueryProperty),
                BindingFlags.Static | BindingFlags.NonPublic,
                new[]
                {
                    typeof(IDictionary<,>).MakeGenericType(typeof(string), typeof(IMtgParameter)),
                    typeof(string),
                    typeof(object)
                })!;

    private readonly ICardService _cardService;
    private readonly MtgApiFlipQuery _flipQuery;

    private readonly int _pageSize;
    private readonly LoadingProgress _loadProgress;

    public MtgApiQuery(
        ICardService cardService,
        MtgApiFlipQuery flipQuery,
        PageSize pageSize,
        LoadingProgress loadProgress)
    {
        _cardService = cardService;
        _flipQuery = flipQuery;
        _pageSize = pageSize.Default;
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
        if (PredicateVisitor.Instance.Visit(predicate) is MethodCallExpression call
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

    private static void QueryProperty(IDictionary<string, IMtgParameter> parameters, string name, object? value)
    {
        if (!parameters.TryGetValue(name, out var parameter))
        {
            parameter = new MtgDefaultParameter(GetParameter(name));
        }

        parameters[name] = parameter.Accept(value);
    }

    private static Expression<Func<CardQueryParameter, string>> GetParameter(string name)
    {
        const BindingFlags binds = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;

        var property = typeof(CardQueryParameter).GetProperty(name, binds);

        if (property == null || property.PropertyType != typeof(string))
        {
            throw new ArgumentException("The parameter property does not exist or is not a string type", nameof(name));
        }

        var param = Expression.Parameter(
            typeof(CardQueryParameter),
            nameof(CardQueryParameter)[0].ToString().ToLower());

        return Expression
            .Lambda<Func<CardQueryParameter, string>>(
                Expression.Property(param, name), param);
    }

    internal async Task<OffsetList<Card>> SearchAsync(
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

        int totalPages = response.PagingInfo.TotalPages;
        var offset = new Offset(currentPage, totalPages);

        var cards = await _flipQuery.GetCardsAsync(response, cancel);

        return new OffsetList<Card>(offset, cards);
    }

    private ICardService ApplyParameters(MtgCardSearch values)
    {
        var cards = _cardService.Where(c => c.Contains, RequiredAttributes);

        foreach (var parameter in values.Parameters.Values)
        {
            cards = parameter.Apply(cards);
        }

        // cards = cards.Where(c => c.OrderBy, "name") get error code 500 with this

        const string pageSize = nameof(CardQuery.PageSize);

        if (values.Parameters.GetValueOrDefault(pageSize)?.IsEmpty ?? true)
        {
            return cards.Where(c => c.PageSize, _pageSize);
        }

        return cards;
    }

    public IAsyncEnumerable<Card> CollectionAsync(IEnumerable<string> multiverseIds)
        => BulkSearchAsync(multiverseIds);

    private async IAsyncEnumerable<Card> BulkSearchAsync(
        IEnumerable<string> multiverseIds,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancel = default)
    {
        const int chunkSize = (int)(Limit * 0.9f); // leave wiggle room for result

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

            string multiverseArgs = string.Join(Or, multiverseChunk);

            var response = await _cardService
                .Where(c => c.MultiverseId, multiverseArgs)
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

    public async Task<Card?> FindAsync(string id, CancellationToken cancel = default)
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
