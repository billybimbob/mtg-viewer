using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using MTGViewer.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class MTGSymbolExtensions
{
    public static IServiceCollection AddSymbols(
        this IServiceCollection services, Action<SymbolOptionsBuilder>? builder = null)
    {
        var optionsBuilder = GetOptionsBuilder(builder);

        services
            .AddOptions<SymbolOptions>()
            .Configure(options =>
            {
                options.DefaultFinder = optionsBuilder.DefaultFinder!;
                options.DefaultTranslator = optionsBuilder.DefaultTranslator!;
            });

        foreach (var handler in optionsBuilder.SymbolHandlers)
        {
            services.AddScoped(handler);
        }

        services
            .AddScoped(DefaultFinderFactory)
            .AddScoped(DefaultTranslatorFactory)
            .AddScoped<SymbolFormatter>();

        return services;
    }

    private static SymbolOptionsBuilder GetOptionsBuilder(Action<SymbolOptionsBuilder>? builder = null)
    {
        var optionsBuilder = new SymbolOptionsBuilder()
            .AddFormatter<CardText>(isDefault: true);

        builder?.Invoke(optionsBuilder);

        return optionsBuilder;
    }

    private static ISymbolFinder DefaultFinderFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<SymbolOptions>>();
        var defaultFinder = options.Value.DefaultFinder;

        return (ISymbolFinder)provider.GetRequiredService(defaultFinder);
    }

    private static ISymbolTranslator DefaultTranslatorFactory(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<SymbolOptions>>();
        var defaultTranslator = options.Value.DefaultTranslator;

        return (ISymbolTranslator)provider.GetRequiredService(defaultTranslator);
    }
}

public class SymbolOptions
{
    public Type DefaultFinder { get; set; } = default!;

    public Type DefaultTranslator { get; set; } = default!;
}

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
