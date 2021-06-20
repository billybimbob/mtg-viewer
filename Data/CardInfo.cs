using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;


namespace MTGViewer.Data
{
    public class Name
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }
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

        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class SuperType
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class Type
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class SubType
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }
}