using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Models
{
    public class Card
    {
        public string Id { get; set; }

        [Required]
        public string Name { get; set; }
        public virtual IList<Name> Names { get; set; }

        public string Layout { get; set; }

        [Display(Name = "Mana")]
        public string ManaCost { get; set; }

        [Display(Name = "Converted Mana")]
        public int? Cmc { get; set; }
        public virtual IList<Color> Colors { get; set; }

        public virtual IList<SuperType> SuperTypes { get; set; }
        public virtual IList<Type> Types { get; set; }
        public virtual IList<SubType> SubTypes { get; set; }

        public string Rarity { get; set; }

        [Display(Name = "Set")]
        public string SetName { get; set; }
        public string Artist { get; set; }

        public string Text { get; set; }
        public string Flavor { get; set; }

        public string Power { get; set; }
        public string Toughness { get; set; }
        public string Loyalty { get; set; }

        [UrlAttribute]
        public string ImageUrl { get; set; }

        public virtual IList<User> Users { get; set; }

        // can replace with model types
        public string Location { get; set; }
    }
}