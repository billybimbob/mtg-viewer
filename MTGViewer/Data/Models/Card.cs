using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using MtgApiManager.Lib.Service;


namespace MTGViewer.Data
{
    public class Card : IQueryParameter
    {
        // not sure about the sha length range
        [RegularExpression(@"^[a-fA-F0-9-]{1,40}")]
        public string Id { get; init; }

        [Required]
        public string MultiverseId { get; init; }

        [Required]
        public string Name { get; init; }

        public ICollection<Name> Names { get; init; } = new HashSet<Name>();

        public string Layout { get; init; }

        [Display(Name = "Mana")]
        public string ManaCost { get; init; }

        [Display(Name = "Converted Mana")]
        public int? Cmc { get; init; }

        public ICollection<Color> Colors { get; init; } = new HashSet<Color>();

        public ICollection<SuperType> SuperTypes { get; init; } = new HashSet<SuperType>();

        public ICollection<Type> Types { get; init; } = new HashSet<Type>();

        public ICollection<SubType> SubTypes { get; init; } = new HashSet<SubType>();

        public string Rarity { get; init; }

        [Display(Name = "Set")]
        public string SetName { get; init; }

        public string Artist { get; init; }

        public string Text { get; init; }

        public string Flavor { get; init; }

        public string Power { get; init; }

        public string Toughness { get; init; }

        public string Loyalty { get; init; }

        [Display(Name = "Image")]
        [UrlAttribute]
        public string ImageUrl { get; init; }

        [JsonIgnore]
        public ICollection<CardAmount> Amounts { get; } = new HashSet<CardAmount>();


        public IReadOnlyList<string> GetManaSymbols()
        {
            if (string.IsNullOrEmpty(ManaCost))
            {
                return new List<string>();
            }

            var matches = Regex.Matches(ManaCost, @"{([^}]+)}");
            return matches
                .Select(m => m.Groups[1].Value.Replace("/", "").ToLower())
                .ToList();
        }

        public bool IsValid()
        {
            var context = new ValidationContext(this);
            return Validator.TryValidateObject(this, context, null);
        }
    }
}