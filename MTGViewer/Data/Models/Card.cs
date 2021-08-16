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
        public string Id { get; set; }

        public string MultiverseId { get; set; }

        [Required]
        public string Name { get; set; }

        public ICollection<Name> Names { get; init; } = new HashSet<Name>();

        public string Layout { get; set; }

        [Display(Name = "Mana")]
        public string ManaCost { get; set; }

        [Display(Name = "Converted Mana")]
        public int? Cmc { get; set; }

        public ICollection<Color> Colors { get; init; } = new HashSet<Color>();

        public ICollection<SuperType> SuperTypes { get; init; } = new HashSet<SuperType>();

        public ICollection<Type> Types { get; init; } = new HashSet<Type>();

        public ICollection<SubType> SubTypes { get; init; } = new HashSet<SubType>();

        public string Rarity { get; set; }

        [Display(Name = "Set")]
        public string SetName { get; set; }

        public string Artist { get; set; }

        public string Text { get; set; }

        public string Flavor { get; set; }

        public string Power { get; set; }

        public string Toughness { get; set; }

        public string Loyalty { get; set; }

        [Display(Name = "Image")]
        [UrlAttribute]
        public string ImageUrl { get; set; }

        [JsonIgnore]
        public ICollection<CardAmount> Amounts { get; } = new HashSet<CardAmount>();

        public IReadOnlyList<string> GetColorSymbols()
        {
            if (string.IsNullOrEmpty(ManaCost))
            {
                return Enumerable.Empty<string>().ToList();
            }

            var matches = Regex.Matches(ManaCost, @"{([^}]+)}");
            return matches
                .Select(m => m.Groups[1].Value.Replace("/", ""))
                .ToList();
        }

        public bool IsValid()
        {
            var context = new ValidationContext(this);
            return Validator.TryValidateObject(this, context, null);
        }
    }
}