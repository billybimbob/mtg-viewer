using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(MultiverseId), IsUnique = true)]
[Index(nameof(ManaValue), nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(ManaCost), nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(Rarity), nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(SetName), nameof(Name), nameof(Id))]
[Index(nameof(Name), nameof(Id))]
public class Card
{
    public required string Id { get; init; }

    [Display(Name = "Multiverse Id")]
    public required string MultiverseId { get; init; }

    public required string Name { get; init; }

    public required string Layout { get; init; }

    [Display(Name = "Mana Cost")]
    public required string? ManaCost { get; init; }

    [Display(Name = "Mana Value")]
    [Range(0f, 1_000_000f)]
    public required float? ManaValue { get; init; }

    public required Color Color { get; init; }

    public required string Type { get; init; }

    [Display(Name = "Set Name")]
    public required string SetName { get; init; }

    public required Rarity Rarity { get; init; }

    public required Flip? Flip { get; init; }

    public required string? Text { get; init; }

    public required string? Flavor { get; init; }

    public required string? Power { get; init; }

    public required string? Toughness { get; init; }

    public required string? Loyalty { get; init; }

    [Display(Name = "Image")]
    [Url]
    public required string ImageUrl { get; init; }

    public required string Artist { get; init; }

    [JsonIgnore]
    public List<Hold> Holds { get; } = new();

    [JsonIgnore]
    public List<Want> Wants { get; } = new();

    [JsonIgnore]
    public List<Giveback> Givebacks { get; } = new();

    [JsonIgnore]
    public List<Suggestion> Suggestions { get; } = new();
}

public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Mythic,
    Special,
    Bonus
}

[Owned]
public class Flip
{
    [Display(Name = "Multiverse Id")]
    public required string MultiverseId { get; init; }

    [Display(Name = "Mana Cost")]
    public required string? ManaCost { get; init; }

    [Display(Name = "Mana Value")]
    [Range(0f, 1_000_000f)]
    public required float? ManaValue { get; init; }

    public required string Type { get; init; }

    public required string? Text { get; init; }

    public required string? Flavor { get; init; }

    public required string? Power { get; init; }

    public required string? Toughness { get; init; }

    public required string? Loyalty { get; init; }

    [Display(Name = "Image")]
    [Url]
    public required string ImageUrl { get; init; }

    public required string Artist { get; init; }
}
