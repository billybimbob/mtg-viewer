using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Services.Symbols;

public interface ISymbolFinder
{
    IReadOnlyList<ManaSymbol> FindMana(string? mtgText);

    IReadOnlyList<LoyaltySymbol> FindLoyalties(string? mtgText);

    IReadOnlyList<SagaSymbol> FindSagas(string? mtgText);
}

public interface ISymbolTranslator
{
    string ManaString(ManaSymbol symbol);

    string LoyaltyString(LoyaltySymbol symbol);

    string SagaString(SagaSymbol symbol);
}

public abstract record TextSymbol(Range Position);

public record ManaSymbol(Range Postion, string Value)
   : TextSymbol(Postion);

public record LoyaltySymbol(Range Postion, string? Direction, string Value)
    : TextSymbol(Postion);

public record SagaSymbol(Range Position, string Value, bool HasNext)
    : TextSymbol(Position);

public static class SymbolExtensions
{
    public static IEnumerable<TextSymbol> FindSymbols(this ISymbolFinder finder, string? mtgText)
    {
        // return Enumerable.Empty<Symbol>()
        //     .Concat(finder.FindMana(mtgText))
        //     .Concat(finder.FindLoyalties(mtgText))
        //     .Concat(finder.FindSagas(mtgText))
        //     .OrderBy(sy => sy.Position.Start.Value);

        // uses merging instead of linq orderby

        if (string.IsNullOrEmpty(mtgText) || finder is null)
        {
            yield break;
        }

        var symbolStrings = new List<IReadOnlyList<TextSymbol>>
        {
            finder.FindMana(mtgText),
            finder.FindLoyalties(mtgText),
            finder.FindSagas(mtgText)
        };

        var currentSymbols = symbolStrings
            .Where(ss => ss.Count > 0)
            .Select(ss =>
            {
                var symbol = ss.GetEnumerator();
                symbol.MoveNext();
                return symbol;
            })
            .ToHashSet();

        while (currentSymbols.Any())
        {
            var symbol = currentSymbols
                .MinBy(sIter => sIter.Current.Position.Start.Value);

            if (symbol is null)
            {
                yield break;
            }

            yield return symbol.Current;

            if (!symbol.MoveNext())
            {
                currentSymbols.Remove(symbol);
                symbol.Dispose();
            }
        }
    }

    public static string SymbolString(this ISymbolTranslator translator, TextSymbol symbol)
    {
        ArgumentNullException.ThrowIfNull(translator);

        return symbol switch
        {
            ManaSymbol mana => translator.ManaString(mana),
            LoyaltySymbol loyalty => translator.LoyaltyString(loyalty),
            SagaSymbol saga => translator.SagaString(saga),

            _ => throw new ArgumentException("Symbol cannot be translated")
        };
    }

    public static string ManaString(
        this ISymbolTranslator translator, string mana)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(mana);

        return translator.ManaString(new ManaSymbol(default, mana));
    }

    public static string LoyaltyString(
        this ISymbolTranslator translator, string? direction, string value)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(value);

        return translator.LoyaltyString(new LoyaltySymbol(default, direction, value));
    }

    public static string SagaString(
        this ISymbolTranslator translator, string saga, bool hasNext = default)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(saga);

        return translator.SagaString(new SagaSymbol(default, saga, hasNext));
    }
}
