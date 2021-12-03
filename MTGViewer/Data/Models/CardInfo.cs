using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MTGViewer.Data;

public class Name
{
    public Name()
    { }

    public Name(string value, string cardId)
    {
        Value = value;
        CardId = cardId;
    }

    [JsonInclude]
    public string Value { get; init; } = null!;

    [JsonIgnore]
    public string CardId { get; init; } = null!;


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

    public Color(string name, string cardId)
    {
        Name = name;
        CardId = cardId;
    }

    [JsonInclude]
    public string Name { get; init; } = null!;

    [JsonIgnore]
    public string CardId { get; init; } = null!;


    public override string ToString() => Name;
}


public class Supertype
{
    public Supertype()
    { }

    public Supertype(string name, string cardId)
    {
        Name = name;
        CardId = cardId;
    }

    [JsonInclude]
    public string Name { get; init; } = null!;

    [JsonIgnore]
    public string CardId { get; init; } = null!;


    public override string ToString() => Name;
}


public class Type
{
    public Type()
    { }

    public Type(string name, string cardId)
    {
        Name = name;
        CardId = cardId;
    }

    [JsonInclude]
    public string Name { get; init; } = null!;

    [JsonIgnore]
    public string CardId { get; init; } = null!;


    public override string ToString() => Name;
}


public class Subtype
{
    public Subtype()
    { }

    public Subtype(string name, string cardId)
    {
        Name = name;
        CardId = cardId;
    }

    [JsonInclude]
    public string Name { get; init; } = null!;

    [JsonIgnore]
    public string CardId { get; init; } = null!;


    public override string ToString() => Name;
}


public static class Rarity
{
    public static readonly IReadOnlyList<string> Values =
        new List<string>()
        {
            "Common",
            "Uncommon",
            "Rare",
            "Mythic",
            "Special"
        };
}