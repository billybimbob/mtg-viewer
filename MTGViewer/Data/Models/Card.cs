using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using MtgApiManager.Lib.Service;

#nullable enable

// adding annotations for validator

namespace MTGViewer.Data
{
    [Index(nameof(Name), nameof(ManaCost))]
    public class Card : IQueryParameter
    {
        // not sure about the sha length range
        [RegularExpression(@"^[a-fA-F0-9-]{1,40}")]
        [Required]
        public string Id { get; init; } = null!;

        [Required]
        public string MultiverseId { get; init; } = null!;

        [Required]
        public string Name { get; init; } = null!;

        public ICollection<Name> Names { get; init; } = new List<Name>();

        [Required]
        public string Layout { get; init; } = null!;

        [Display(Name = "Mana")]
        [Required]
        public string ManaCost { get; init; } = null!;

        [Display(Name = "Converted Mana")]
        public int? Cmc { get; init; }

        public ICollection<Color> Colors { get; init; } = new List<Color>();

        public ICollection<SuperType> SuperTypes { get; init; } = new List<SuperType>();

        public ICollection<Type> Types { get; init; } = new List<Type>();

        public ICollection<SubType> SubTypes { get; init; } = new List<SubType>();

        [Required]
        public string Rarity { get; init; } = null!;

        [Display(Name = "Set")]
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
        public ICollection<CardAmount> Amounts { get; } = new List<CardAmount>();


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