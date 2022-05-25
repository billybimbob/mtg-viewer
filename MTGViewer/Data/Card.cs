using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;

[Index(nameof(MultiverseId), IsUnique = true)]
[Index(nameof(ManaValue), nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(ManaCost), nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(Rarity), nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(Name), nameof(Id))]
public class Card
{
    public string Id { get; init; } = default!;

    [Display(Name = "Multiverse Id")]
    public string MultiverseId { get; init; } = default!;

    public string Name { get; init; } = default!;

    public string Layout { get; init; } = default!;

    [Display(Name = "Mana Cost")]
    public string? ManaCost { get; init; } = default!;

    [Display(Name = "Mana Value")]
    [Range(0f, 1_000_000f)]
    public float? ManaValue { get; init; }

    public Color Color { get; init; }

    public string Type { get; init; } = default!;

    [Display(Name = "Set Name")]
    public string SetName { get; init; } = default!;

    public Rarity Rarity { get; init; }

    public Flip? Flip { get; init; }

    public string? Text { get; init; }

    public string? Flavor { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }

    [Display(Name = "Image")]
    [Url]
    public string ImageUrl { get; init; } = default!;

    public string Artist { get; init; } = default!;

    [JsonIgnore]
    public List<Hold> Holds { get; } = new();

    [JsonIgnore]
    public List<Want> Wants { get; } = new();

    [JsonIgnore]
    public List<Giveback> Givebacks { get; } = new();

    [JsonIgnore]
    public List<Suggestion> Suggestions { get; } = new();
}

[Owned]
public class Flip
{
    [Display(Name = "Multiverse Id")]
    public string MultiverseId { get; init; } = default!;

    [Display(Name = "Mana Cost")]
    public string? ManaCost { get; init; } = default!;

    [Display(Name = "Mana Value")]
    [Range(0f, 1_000_000f)]
    public float? ManaValue { get; init; }

    public string Type { get; init; } = default!;

    public string? Text { get; init; }

    public string? Flavor { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }

    [Display(Name = "Image")]
    [Url]
    public string ImageUrl { get; init; } = default!;

    public string Artist { get; init; } = default!;
}
