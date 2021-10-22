using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

#nullable enable

// adding annotations for validator

namespace MTGViewer.Data
{
    [Index(nameof(Name), nameof(ManaCost))]
    public class Card
    {
        [Required]
        public string Id { get; init; } = null!;

        [Required]
        [Display(Name = "Multiverse Id")]
        public string MultiverseId { get; init; } = null!;


        [Required]
        public string Name { get; set; } = null!;

        public List<Name> Names { get; init; } = new();

        [Required]
        public string Layout { get; init; } = null!;


        [Display(Name = "Mana")]
        [Required]
        public string ManaCost { get; set; } = null!;

        [Display(Name = "Converted Mana Cost")]
        [Range(0f, 1_000_000f)]
        public float? Cmc { get; set; }

        public List<Color> Colors { get; init; } = new();


        public List<Supertype> Supertypes { get; init; } = new();

        public List<Type> Types { get; init; } = new();

        public List<Subtype> Subtypes { get; init; } = new();


        [Required]
        public string Rarity { get; set; } = null!;

        [Display(Name = "Set Name")]
        [Required]
        public string SetName { get; set; } = null!;

        [Required]
        public string Artist { get; set; } = null!;


        public string? Text { get; init; }

        public string? Flavor { get; init; }

        public string? Power { get; set; }

        public string? Toughness { get; set; }

        public string? Loyalty { get; set; }


        [Display(Name = "Image")]
        [Url]
        public string? ImageUrl { get; init; }

        [JsonIgnore]
        public List<CardAmount> Amounts { get; } = new();


        public bool IsValid()
        {
            var context = new ValidationContext(this);
            return Validator.TryValidateObject(this, context, null);
        }
    }
}