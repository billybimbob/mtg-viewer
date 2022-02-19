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
    private readonly ImmutableDictionary<string, object?> _parameters;

    public MtgCardSearch(
        MtgApiQuery provider,
        IReadOnlyDictionary<string, object?> parameters)
    {
        _provider = provider;
        _parameters = parameters.ToImmutableDictionary();
    }

    public IReadOnlyDictionary<string, object?> Parameters => _parameters;

    public int Page => (Parameters
        .GetValueOrDefault(nameof(CardQuery.Page)) as int?) ?? 0;


    public bool IsEmpty()
    {
        foreach (var value in Parameters.Values)
        {
            bool notEmpty = value switch
            {
                IEnumerable<string> ie => ie.Any(),
                string s => !string.IsNullOrWhiteSpace(s),
                int i => i > 0,
                _ => false
            };

            if (notEmpty)
            {
                return false;
            }
        }

        return true;
    }

    public ImmutableDictionary<string, object?>.Builder ToBuilder()
    {
        return _parameters.ToBuilder();
    }

    public IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        return _provider.Where(this, predicate);
    }

    public ValueTask<OffsetList<Card>> SearchAsync(CancellationToken cancel = default)
    {
        return _provider.SearchAsync(this, cancel);
    }
}