using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MTGViewer.Data;

namespace MTGViewer.Services;


public class CardText : ISymbolFinder, ISymbolTranslator
{
    private const string Mana = $@"{{(?<{ nameof(Mana) }>[^}}]+)}}";

    private const string Direction = "direction";
    private const string Loyalty = $@"\[(?<{ Direction }>[+−])?(?<{ nameof(Loyalty) }>\d+)\]";

    private const string Saga = $@"(?<{ nameof(Saga) }>(?:[IV]+(?:, )?)+) —";


    public IReadOnlyList<ManaSymbol> FindMana(string? mtgText)
    {
        if (mtgText is null)
        {
            return Array.Empty<ManaSymbol>();
        }

        return Regex
            .Matches(mtgText, Mana)
            .Select(m =>
            {
                var mana = m.Groups[nameof(Mana)];

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
            .Matches(mtgText, Loyalty)
            .Select(m =>
            {
                var direction = m.Groups[Direction];

                var directionValue = direction.Success 
                    ? direction.Value 
                    : null;

                var loyalty = m.Groups[nameof(Loyalty)];

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
            .Matches(mtgText, Saga)
            .SelectMany(m =>
            {
                const string separator = ", ";
                var sagaGroup = m.Groups[nameof(Saga)];

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

    public string ColorString(Color color)
    {
        var manaSymbols = Enum.GetValues<Color>()
            .Where(c => c is not Color.None && color.HasFlag(c))
            .Select(c => ManaString(new ManaSymbol(default, Symbol.Colors[c])));

        return string.Join(string.Empty, manaSymbols);
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