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

        public int Id { get; set; }

        public string Value { get; set; }

        [JsonIgnore]
        public Card Card { get; set; } = null!;
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

        public int Id { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        public Card Card { get; set; } = null!;
    }


    public class SuperType
    {
        public SuperType(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        public Card Card { get; set; } = null!;
    }


    public class Type
    {
        public Type(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        public Card Card { get; set; } = null!;
    }


    public class SubType
    {
        public SubType(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        public Card Card { get; set; } = null!;
    }
}