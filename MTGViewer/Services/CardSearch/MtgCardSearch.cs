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


internal readonly struct MtgCardSearch : IMTGCardSearch
{
    private readonly MtgApiQuery? _provider;
    private readonly ImmutableDictionary<string, object?>? _parameters;

    public MtgCardSearch(
        MtgApiQuery provider,
        IReadOnlyDictionary<string, object?> parameters)
    {
        _provider = provider;
        _parameters = parameters.ToImmutableDictionary();
    }

    private ImmutableDictionary<string, object?> Values =>
        _parameters ?? ImmutableDictionary<string, object?>.Empty;

    public IReadOnlyDictionary<string, object?> Parameters => Values;

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


    public IMTGCardSearch Where(Expression<Func<CardQuery, bool>> predicate)
    {
        // boxes the struct, so really no point
        return _provider?.Where(this, predicate) ?? new MtgCardSearch();
    }


    public ValueTask<OffsetList<Card>> SearchAsync(CancellationToken cancel = default)
    {
        return _provider?.SearchAsync(this, cancel)
            ?? ValueTask.FromResult(OffsetList<Card>.Empty());
    }


    public ImmutableDictionary<string, object?>.Builder ToBuilder() => Values.ToBuilder();
}