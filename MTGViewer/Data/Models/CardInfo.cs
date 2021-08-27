using System.Collections.Generic;
using Newtonsoft.Json;

#nullable enable

namespace MTGViewer.Data
{
    public class Name
    {
        public Name(string value)
        {
            Value = value;
        }

        [JsonProperty]
        public int Id { get; private set; }

        [JsonProperty]
        public string Value { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }


    public class Color
    {
        [JsonIgnore]
        public static readonly IReadOnlyDictionary<string, string> COLORS = 
            new Dictionary<string, string>
        {
            ["black"] = "b",
            ["blue"] = "u",
            ["green"] = "g",
            ["red"] = "r",
            ["white"] = "w"
        };

        public Color(string name)
        {
            Name = name;
        }

        [JsonProperty]
        public int Id { get; private set; }

        [JsonProperty]
        public string Name { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }


    public class SuperType
    {
        public SuperType(string name)
        {
            Name = name;
        }

        [JsonProperty]
        public int Id { get; private set; }

        [JsonProperty]
        public string Name { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }


    public class Type
    {
        public Type(string name)
        {
            Name = name;
        }

        [JsonProperty]
        public int Id { get; private set; }

        [JsonProperty]
        public string Name { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }


    public class SubType
    {
        public SubType(string name)
        {
            Name = name;
        }

        [JsonProperty]
        public int Id { get; private set; }

        [JsonProperty]
        public string Name { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }
}