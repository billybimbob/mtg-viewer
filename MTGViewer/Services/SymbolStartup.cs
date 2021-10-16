using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace MTGViewer.Services
{
    public static class MTGSymbolExtensions
    {
        public static IServiceCollection AddSymbols(
            this IServiceCollection services, Action<SymbolOptions>? options = null)
        {
            var symbolOptions = new SymbolOptions()
                .AddTranslator<CardTextTranslator>();

            options?.Invoke(symbolOptions);

            services.AddScoped<MTGSymbols>();

            foreach(var translator in symbolOptions.Translators)
            {
                services.AddScoped(translator);
            }

            return services;
        }
    }


    public class SymbolOptions
    {
        private readonly List<Type> _translators = new();

        public IEnumerable<Type> Translators => _translators.Distinct();

        public SymbolOptions AddTranslator(Type type)
        {
            if (type.IsAssignableTo(typeof(ISymbolTranslator)))
            {
                _translators.Add(type);
            }

            return this;
        }

        public SymbolOptions AddTranslator<T>() where T : ISymbolTranslator
        {
            return AddTranslator(typeof(T));
        }
    }
}