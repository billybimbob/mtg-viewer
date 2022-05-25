using System;
using System.Collections.Generic;

namespace MTGViewer.Data;

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Mythic,
    Special,
    Bonus
}

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

    private static SortedList<Color, string>? _colors;

    public static IReadOnlyDictionary<Color, string> Colors =>
        _colors ??= new()
        {
            [Color.Black] = "B",
            [Color.Blue] = "U",
            [Color.Green] = "G",
            [Color.Red] = "R",
            [Color.White] = "W"
        };
}

internal enum LocationType
{
    Invalid,
    Deck,
    Unclaimed,
    Box,
    Excess
}

internal enum QuantityType
{
    Invalid,
    Hold,
    Want,
    Giveback
}
