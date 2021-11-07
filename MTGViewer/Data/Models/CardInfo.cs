using System.Collections.Generic;
using System.Text.Json.Serialization;

#nullable enable

namespace MTGViewer.Data
{
    public class Name
    {
        public Name(string value)
        {
            Value = value;
        }

        [JsonInclude]
        public string Value { get; private set; }

        [JsonIgnore]
        public string CardId { get; private set; } = null!;


        public override string ToString() => Value;
    }


    public class Color
    { 
        public static readonly IReadOnlyDictionary<string, string> Symbols = 
            new SortedList<string, string>
            {
                ["black"] = "B",
                ["blue"]  = "U",
                ["green"] = "G",
                ["red"]   = "R",
                ["white"] = "W"
            };

        public Color(string name)
        {
            Name = name;
        }

        [JsonInclude]
        public string Name { get; private set; }

        [JsonIgnore]
        public string CardId { get; private set; } = null!;


        public override string ToString() => Name;
    }


    public class Supertype
    {
        public Supertype(string name)
        {
            Name = name;
        }

        [JsonInclude]
        public string Name { get; private set; }

        [JsonIgnore]
        public string CardId { get; private set; } = null!;


        public override string ToString() => Name;
    }


    public class Type
    {
        public Type(string name)
        {
            Name = name;
        }

        [JsonInclude]
        public string Name { get; private set; }

        [JsonIgnore]
        public string CardId { get; private set; } = null!;


        public override string ToString() => Name;
    }


    public class Subtype
    {
        public Subtype(string name)
        {
            Name = name;
        }

        [JsonInclude]
        public string Name { get; private set; }

        [JsonIgnore]
        public string CardId { get; private set; } = null!;


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
}