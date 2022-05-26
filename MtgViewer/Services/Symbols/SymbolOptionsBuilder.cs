using System;
using System.Collections.Generic;

namespace MtgViewer.Services.Symbols;

public class SymbolOptionsBuilder
{
    private readonly HashSet<Type> _handlers = new();

    public Type? DefaultFinder { get; private set; }
    public Type? DefaultTranslator { get; private set; }

    public IEnumerable<Type> SymbolHandlers => _handlers;

    public SymbolOptionsBuilder AddFinder(Type finder, bool isDefault = false)
    {
        if (!finder.IsAssignableTo(typeof(ISymbolFinder)))
        {
            throw new ArgumentException(
                $"Give type {finder} is not a valid {typeof(ISymbolFinder)}");
        }

        _handlers.Add(finder);

        if (isDefault)
        {
            DefaultFinder = finder;
        }

        return this;
    }

    public SymbolOptionsBuilder AddFinder<TFinder>(bool isDefault = false)
        where TFinder : ISymbolFinder
        => AddFinder(typeof(TFinder), isDefault);

    public SymbolOptionsBuilder AddTranslator(Type translator, bool isDefault = false)
    {
        if (!translator.IsAssignableTo(typeof(ISymbolTranslator)))
        {
            throw new ArgumentException(
                $"Give type {translator} is not a valid {typeof(ISymbolTranslator)}");
        }

        _handlers.Add(translator);

        if (isDefault)
        {
            DefaultTranslator = translator;
        }

        return this;
    }

    public SymbolOptionsBuilder AddTranslator<TTranslator>(bool isDefault = false)
        where TTranslator : ISymbolTranslator
        => AddTranslator(typeof(TTranslator), isDefault);

    public SymbolOptionsBuilder AddFormatter(Type formatter, bool isDefault = false)
    {
        if (!formatter.IsAssignableTo(typeof(ISymbolFinder))
            || !formatter.IsAssignableTo(typeof(ISymbolTranslator)))
        {
            throw new ArgumentException(
                $"Give type {formatter} is not a valid formatter");
        }

        _handlers.Add(formatter);

        if (isDefault)
        {
            DefaultFinder = formatter;
            DefaultTranslator = formatter;
        }

        return this;
    }

    public SymbolOptionsBuilder AddFormatter<TFormatter>(bool isDefault = false)
        where TFormatter : ISymbolFinder, ISymbolTranslator
        => AddFormatter(typeof(TFormatter), isDefault);
}
