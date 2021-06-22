using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;

namespace MTGViewer.Data
{
    public class Card
    {
        // not sure about the sha length range
        [RegularExpression(@"^[a-fA-F0-9-]{1,40}")]
        public string Id { get; set; }

        [Required]
        public string Name { get; set; }

        public ISet<Name> Names { get; set; } = new HashSet<Name>();

        public string Layout { get; set; }

        [Display(Name = "Mana")]
        public string ManaCost { get; set; }

        [Display(Name = "Converted Mana")]
        public int? Cmc { get; set; }

        public ISet<Color> Colors { get; set; } = new HashSet<Color>();

        public ISet<SuperType> SuperTypes { get; set; } = new HashSet<SuperType>();

        public ISet<Type> Types { get; set; } = new HashSet<Type>();

        public ISet<SubType> SubTypes { get; set; } = new HashSet<SubType>();

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

        // locations can be derived from amounts
        // could possibly derive amounts from locations
        public ISet<CardAmount> Amounts { get; } = new HashSet<CardAmount>();

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