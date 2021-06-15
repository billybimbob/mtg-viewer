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
        public IList<Name> Names { get; set; }

        public string Layout { get; set; }

        [Display(Name = "Mana")]
        public string ManaCost { get; set; }

        [Display(Name = "Converted Mana")]
        public int? Cmc { get; set; }
        public IList<Color> Colors { get; set; }

        public IList<SuperType> SuperTypes { get; set; }
        public IList<Type> Types { get; set; }
        public IList<SubType> SubTypes { get; set; }

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
        public IList<CardAmount> Amounts { get; set; }


        public IReadOnlyList<string> GetColorSymbols()
        {
            if (string.IsNullOrEmpty(ManaCost))
            {
                return Enumerable.Empty<string>().ToList();
            }

            var matches = Regex.Matches(ManaCost, @"{([^}]+)}");
            return matches.Select(m => m.Groups[1].Value).ToList();
        }


        public bool IsValid()
        {
            var context = new ValidationContext(this);
            return Validator.TryValidateObject(this, context, null);
        }
    }
}