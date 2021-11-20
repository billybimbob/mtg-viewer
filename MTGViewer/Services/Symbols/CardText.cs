using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MTGViewer.Services;


public class CardText : ISymbolFinder, ISymbolTranslator
{
    private const string _mana = $@"{{(?<{ nameof(_mana) }>[^}}]+)}}";

    private const string _direction = "direction";
    private const string _loyalty = $@"\[(?<{ _direction }>[+−])?(?<{ nameof(_loyalty) }>\d+)\]";

    private const string _saga = $@"(?<{ nameof(_saga) }>(?:[IV]+(?:, )?)+) —";


    public IReadOnlyList<ManaSymbol> FindMana(string? mtgText)
    {
        if (mtgText is null)
        {
            return Array.Empty<ManaSymbol>();
        }

        return Regex
            .Matches(mtgText, _mana)
            .Select(m =>
            {
                var mana = m.Groups[nameof(_mana)];

                var start = m.Index;
                var end = m.Index + m.Length;

                return new ManaSymbol(start..end, mana.Value);
            })
            .ToList();
    }


    public IReadOnlyList<LoyaltySymbol> FindLoyalties(string? mtgText)
    {
        if (mtgText is null)
        {
            return Array.Empty<LoyaltySymbol>();
        }

        return Regex
            .Matches(mtgText, _loyalty)
            .Select(m =>
            {
                var direction = m.Groups[_direction];

                var directionValue = direction.Success 
                    ? direction.Value 
                    : null;

                var loyalty = m.Groups[nameof(_loyalty)];

                var start = m.Index;
                var end = m.Index + m.Length;

                return new LoyaltySymbol(
                    start..end, directionValue, loyalty.Value);
            })
            .ToList();
    }


    public IReadOnlyList<SagaSymbol> FindSagas(string? mtgText)
    {
        if (mtgText is null)
        {
            return Array.Empty<SagaSymbol>();
        }

        return Regex
            .Matches(mtgText, _saga)
            .SelectMany(m =>
            {
                var sagaGroup = m.Groups[nameof(_saga)];
                var separator = ", ";

                var sagas = sagaGroup.Value.Split(separator);
                var indices = SagaIndices(sagas, sagaGroup.Index, separator);

                return sagas
                    .Zip(indices, (saga, index) => (saga, index))
                    .Select(si =>
                    {
                        var hasNext = si.saga != sagas[^1];

                        var start = si.index; 
                        var end = hasNext
                            ? si.index + si.saga.Length + separator.Length
                            : m.Index + m.Length;

                        return new SagaSymbol(start..end, si.saga, hasNext);
                    });
            })
            .ToList();
    }


    private IReadOnlyList<int> SagaIndices(string[] sagas, int start, string separator)
    {
        var indices = new List<int> { start };

        foreach (var saga in sagas.SkipLast(1))
        {
            indices.Add( indices[^1] + saga.Length + separator.Length );
        }

        return indices;
    }


    public string ManaString(ManaSymbol symbol)
    {
        return $"{{{symbol.Value}}}";
    }


    public string LoyaltyString(LoyaltySymbol symbol)
    {
        var (_, direction, loyalty) = symbol;

        return $"[{direction}{symbol}]";
    }


    public string SagaString(SagaSymbol symbol)
    {
        var (_, saga, hasNext) = symbol;

        return hasNext ? $"{saga}, " : $"{saga} —";
    }
}