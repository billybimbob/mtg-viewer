using System;
using System.Collections.Generic;
using System.Text;

#nullable enable
namespace MTGViewer.Services;


public class SymbolFormatter : ISymbolFinder, ISymbolTranslator
{
    private readonly ISymbolFinder _finder;
    private readonly ISymbolTranslator _translator;

    public SymbolFormatter(ISymbolFinder finder, ISymbolTranslator translator)
    {
        _finder = finder;
        _translator = translator;
    }


    public string Format(string mtgText)
    {
        if (string.IsNullOrWhiteSpace(mtgText))
        {
            return string.Empty;
        }

        var translation = new StringBuilder();
        int lastSymbol = 0;

        foreach (var symbol in _finder.FindSymbols(mtgText))
        {
            int lastLength = symbol.Position.Start.Value - lastSymbol;
            var symbolString = _translator.SymbolString(symbol);

            translation
                .Append(mtgText, lastSymbol, lastLength)
                .Append(symbolString);

            lastSymbol = symbol.Position.End.Value;
        }

        int remaining = mtgText.Length - lastSymbol;

        if (remaining > 0)
        {
            translation.Append(mtgText, lastSymbol, remaining);
        }

        return translation.ToString();
    }


    public IReadOnlyList<ManaSymbol> FindMana(string mtgText) => 
        _finder.FindMana(mtgText);

    public IReadOnlyList<LoyaltySymbol> FindLoyalties(string mtgText) => 
        _finder.FindLoyalties(mtgText);

    public IReadOnlyList<SagaSymbol> FindSagas(string mtgText) => 
        _finder.FindSagas(mtgText);


    public string ManaString(ManaSymbol symbol) => 
        _translator.ManaString(symbol);

    public string LoyaltyString(LoyaltySymbol symbol) => 
        _translator.LoyaltyString(symbol);

    public string SagaString(SagaSymbol symbol) => 
        _translator.SagaString(symbol);
}



public static class ComposeExtensions
{
    public static SymbolFormatter ComposeWith(
        this ISymbolFinder finder, ISymbolTranslator translator)
    {
        if (translator is null)
        {
            throw new ArgumentNullException("Translator is null");
        }

        if (finder is null)
        {
            throw new ArgumentNullException("Finder is null");
        }

        return new SymbolFormatter(finder, translator);
    }


    public static SymbolFormatter ComposeWith(
        this ISymbolTranslator translator, ISymbolFinder finder)
    {
        if (translator is null)
        {
            throw new ArgumentNullException("Translator is null");
        }

        if (finder is null)
        {
            throw new ArgumentNullException("Finder is null");
        }

        return new SymbolFormatter(finder, translator);
    }
}