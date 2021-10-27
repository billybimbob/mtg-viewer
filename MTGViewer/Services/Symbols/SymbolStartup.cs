using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

#nullable enable

namespace MTGViewer.Services
{
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

            foreach(var handler in optionsBuilder.SymbolHandlers)
            {
                services.AddScoped(handler);
            }

            services
                .AddScoped<ISymbolFinder>(DefaultFinderFactory)
                .AddScoped<ISymbolTranslator>(DefaultTranslatorFactory)
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
        public Type DefaultFinder { get; set; } = null!;

        public Type DefaultTranslator { get; set; } = null!;
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


        public SymbolOptionsBuilder AddFinder<F>(bool isDefault = false) 
            where F : ISymbolFinder
        {
            return AddFinder(typeof(F), isDefault);
        }


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


        public SymbolOptionsBuilder AddTranslator<T>(bool isDefault = false) 
            where T : ISymbolTranslator
        {
            return AddTranslator(typeof(T), isDefault);
        }


        public SymbolOptionsBuilder AddFormatter(Type formatter, bool isDefault = false)
        {
            if ( !formatter.IsAssignableTo(typeof(ISymbolFinder))
                || !formatter.IsAssignableTo(typeof(ISymbolTranslator)) )
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

        public SymbolOptionsBuilder AddFormatter<F>(bool isDefault = false)
            where F : ISymbolFinder, ISymbolTranslator
        {
            return AddFormatter(typeof(F), isDefault);
        }
    }
}