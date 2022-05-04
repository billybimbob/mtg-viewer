using System;

namespace MTGViewer.Services.Symbols;

public class SymbolOptions
{
    public Type DefaultFinder { get; set; } = default!;

    public Type DefaultTranslator { get; set; } = default!;
}
