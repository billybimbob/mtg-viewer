using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;
using MTGViewer.Data;

namespace MTGViewer.Services;

internal class MtgCardSearch : IMTGCardSearch
{
    private readonly MtgApiQuery _provider;
    private readonly ImmutableDictionary<string, IMtgParameter> _parameters;

    public MtgCardSearch(
        MtgApiQuery provider,
        IDictionary<string, IMtgParameter> parameters)
    {
        _provider = provider;
        _parameters = parameters.ToImmutableDictionary();
    }

    public IReadOnlyDictionary<string, IMtgParameter> Parameters => _parameters;

    public int Page =>
        (_parameters.GetValueOrDefault(nameof(CardQuery.Page)) as MtgPageParameter)
        ?.Page ?? 0;


    public bool IsEmpty => _parameters.Values.All(p => p.IsEmpty);


    public IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        return _provider
            .QueryFromPredicate(_parameters.ToBuilder(), predicate);
    }

    public ValueTask<OffsetList<Card>> SearchAsync(CancellationToken cancel = default)
    {
        return _provider.SearchAsync(this, cancel);
    }
}