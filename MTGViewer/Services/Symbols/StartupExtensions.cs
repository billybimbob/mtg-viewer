using System;

using Microsoft.Extensions.Options;

using MTGViewer.Services.Symbols;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class StartupExtensions
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
