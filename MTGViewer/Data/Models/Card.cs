using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;

// adding annotations for validator

[Index(nameof(Name), nameof(SetName), nameof(Id))]
[Index(nameof(MultiverseId))]
public class Card
{
    [Key]
    public string Id { get; init; } = default!;

    [Required]
    [Display(Name = "Multiverse Id")]
    public string MultiverseId { get; init; } = default!;


    [Required]
    public string Name { get; init; } = default!;

    public List<Name> Names { get; init; } = new();

    [Required]
    public string Layout { get; init; } = default!;


    [Display(Name = "Mana Cost")]
    public string? ManaCost { get; init; } = default!;

    [Display(Name = "Converted Mana Cost")]
    [Range(0f, 1_000_000f)]
    public float? Cmc { get; init; }

    public List<Color> Colors { get; init; } = new();


    public List<Supertype> Supertypes { get; init; } = new();

    public List<Type> Types { get; init; } = new();

    public List<Subtype> Subtypes { get; init; } = new();


    [Required]
    public Rarity Rarity { get; init; }

    [Display(Name = "Set Name")]
    [Required]
    public string SetName { get; init; } = default!;

    [Required]
    public string Artist { get; init; } = default!;


    public string? Text { get; init; }

    public string? Flavor { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }


    [Required]
    [Display(Name = "Image")]
    [Url]
    public string ImageUrl { get; init; } = default!;

    [JsonIgnore]
    public List<Amount> Amounts { get; } = new();

    [JsonIgnore]
    public List<Want> Wants { get; } = new();

    [JsonIgnore]
    public List<Suggestion> Suggestions { get; } = new();


    public bool IsValid()
    {
        var context = new ValidationContext(this);
        return Validator.TryValidateObject(this, context, null);
    }
}