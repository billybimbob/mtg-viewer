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

        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
        public string Value { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }


    public class Color
    { 
        public Color(string name)
        {
            Name = name;
        }

        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
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

        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
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

        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
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

        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
        public string Name { get; private set; }

        [JsonIgnore]
        public Card Card { get; private set; } = null!;
    }
}