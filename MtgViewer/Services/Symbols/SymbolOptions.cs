using System;

namespace MtgViewer.Services.Symbols;

public class SymbolOptions
{
    public required Type DefaultFinder { get; set; }

    public required Type DefaultTranslator { get; set; }
}
