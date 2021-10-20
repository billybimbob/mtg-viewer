using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable

namespace MTGViewer.Services
{
    public interface ISymbolFinder
    {
        IReadOnlyList<ManaSymbol> FindMana(string mtgText);

        IReadOnlyList<LoyaltySymbol> FindLoyalties(string mtgText);

        IReadOnlyList<SagaSymbol> FindSagas(string mtgText);
    }


    public interface ISymbolTranslator
    {
        string ManaString(ManaSymbol symbol);

        string LoyaltyString(LoyaltySymbol symbol);

        string SagaString(SagaSymbol symbol);
    }


    public abstract record Symbol(Range Position);

    public record ManaSymbol(Range Postion, string Value) 
        : Symbol(Postion);

    public record LoyaltySymbol(Range Postion, string? Direction, string Value)
        : Symbol(Postion);

    public record SagaSymbol(Range Position, string Value, bool HasNext)
        : Symbol(Position);



    public static class FinderExtensions
    {
        public static IEnumerable<Symbol> FindSymbols(this ISymbolFinder finder, string mtgText)
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

            var symbolStrings = new List<IReadOnlyList<Symbol>>
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
                var symbol = FirstSymbol();

                yield return symbol.Current;

                if (!symbol.MoveNext())
                {
                    currentSymbols.Remove(symbol);
                    symbol.Dispose();
                }
            }

            IEnumerator<Symbol> FirstSymbol()
            {
                IEnumerator<Symbol> first = currentSymbols.First();

                // not skipping since hashset iter order is not defined
                foreach (var iter in currentSymbols)
                {
                    var iterPosition = iter.Current.Position.Start.Value;
                    var minPosition = first?.Current.Position.Start.Value;

                    if (iterPosition < minPosition)
                    {
                        first = iter;
                    }
                }

                return first;
            }
        }
    }


    public static class TranslatorExtensions
    {
        public static string SymbolString(this ISymbolTranslator translator, Symbol symbol)
        {
            if (translator is null)
            {
                throw new ArgumentNullException("Translator is null");
            }

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
            if (translator is null)
            {
                throw new ArgumentNullException("Translator is null");
            }

            return translator.ManaString(new ManaSymbol(default, mana));
        }


        public static string LoyaltyString(
            this ISymbolTranslator translator, string? direction, string value)
        {
            if (translator is null)
            {
                throw new ArgumentNullException("Translator is null");
            }

            return translator.LoyaltyString(new LoyaltySymbol(default, direction, value));
        }


        public static string SagaString(
            this ISymbolTranslator translator, string saga)
        {
            if (translator is null)
            {
                throw new ArgumentNullException("Translator is null");
            }

            return translator.SagaString(new SagaSymbol(default, saga, default));
        }
    }
}