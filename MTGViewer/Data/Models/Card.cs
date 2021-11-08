using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

#nullable enable

// adding annotations for validator

namespace MTGViewer.Data
{
    [Index(nameof(Name))]
    public class Card
    {
        [Required]
        public string Id { get; init; } = null!;

        [Required]
        [Display(Name = "Multiverse Id")]
        public string MultiverseId { get; init; } = null!;


        [Required]
        public string Name { get; init; } = null!;

        public List<Name> Names { get; init; } = new();

        [Required]
        public string Layout { get; init; } = null!;


        [Display(Name = "Mana Cost")]
        [Required]
        public string ManaCost { get; init; } = null!;

        [Display(Name = "Converted Mana Cost")]
        [Range(0f, 1_000_000f)]
        public float? Cmc { get; init; }

        public List<Color> Colors { get; init; } = new();


        public List<Supertype> Supertypes { get; init; } = new();

        public List<Type> Types { get; init; } = new();

        public List<Subtype> Subtypes { get; init; } = new();


        [Required]
        public string Rarity { get; init; } = null!;

        [Display(Name = "Set Name")]
        [Required]
        public string SetName { get; init; } = null!;

        [Required]
        public string Artist { get; init; } = null!;


        public string? Text { get; init; }

        public string? Flavor { get; init; }

        public string? Power { get; init; }

        public string? Toughness { get; init; }

        public string? Loyalty { get; init; }


        [Display(Name = "Image")]
        [Url]
        public string? ImageUrl { get; init; }

        [JsonIgnore]
        public List<Amount> Amounts { get; } = new();

        [JsonIgnore]
        public List<Want> Wants { get; } = new();


        public bool IsValid()
        {
            var context = new ValidationContext(this);
            return Validator.TryValidateObject(this, context, null);
        }
    }
}