using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MTGViewer.Data;

public class Name
{
    public Name()
    { }


    [JsonInclude]
    public string Value { get; init; } = default!;

    [JsonInclude]
    public string CardId { get; init; } = default!;


    public override string ToString() => Value;
}


public class Color
{ 
    public static readonly IReadOnlyDictionary<string, string> Symbols = 
        new SortedList<string, string>
        {
            ["B"] = "Black",
            ["G"] = "Green",
            ["R"] = "Red",
            ["U"] = "Blue",
            ["W"] = "White"
        };

    public Color()
    { }

    [JsonInclude]
    public string Name { get; init; } = default!;

    [JsonInclude]
    public string CardId { get; init; } = default!;


    public override string ToString() => Name;
}


public class Supertype
{
    public Supertype()
    { }

    [JsonInclude]
    public string Name { get; init; } = default!;

    [JsonInclude]
    public string CardId { get; init; } = default!;


    public override string ToString() => Name;
}


public class Type
{
    public Type()
    { }

    [JsonInclude]
    public string Name { get; init; } = default!;

    [JsonInclude]
    public string CardId { get; init; } = default!;


    public override string ToString() => Name;
}


public class Subtype
{
    public Subtype()
    { }

    [JsonInclude]
    public string Name { get; init; } = default!;

    [JsonInclude]
    public string CardId { get; init; } = default!;


    public override string ToString() => Name;
}


public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Mythic,
    Special
}