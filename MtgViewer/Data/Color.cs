using System;
using System.Collections.Generic;

namespace MtgViewer.Data;

[Flags]
public enum Color
{
    None = 0,
    Black = 2,
    Blue = 4,
    Green = 8,
    Red = 16,
    White = 32
}

public static class Symbol
{
    public const string LongDash = "\u2014";

    public static IReadOnlyDictionary<Color, string> Colors { get; }
        = new SortedList<Color, string>()
        {
            [Color.Black] = "B",
            [Color.Blue] = "U",
            [Color.Green] = "G",
            [Color.Red] = "R",
            [Color.White] = "W"
        };
}
