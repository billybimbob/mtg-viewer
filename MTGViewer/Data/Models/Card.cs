using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Data;

[Index(nameof(Color), nameof(Name), nameof(SetName), nameof(Id))]
[Index(nameof(Name), nameof(SetName), nameof(Id))]
[Index(nameof(MultiverseId))]
public class Card
{
    [Key]
    public string Id { get; init; } = default!;

    [Display(Name = "Multiverse Id")]
    public string MultiverseId { get; init; } = default!;

    public string Name { get; init; } = default!;

    public string Layout { get; init; } = default!;


    [Display(Name = "Mana Cost")]
    public string? ManaCost { get; init; } = default!;

    [Display(Name = "Converted Mana Cost")]
    [Range(0f, 1_000_000f)]
    public float? Cmc { get; init; }

    public Color Color { get; init; }

    public string Type { get; init; } = default!;

    [Display(Name = "Set Name")]
    public string SetName { get; init; } = default!;

    public Rarity Rarity { get; init; }

    public Flip? Flip { get; set; }


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
    public List<Amount> Amounts { get; } = new();

    [JsonIgnore]
    public List<Want> Wants { get; } = new();

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


[Flags]
public enum Color
{
    None = 0,
    Black = 2,
    Blue = 4,
    Green = 8,
    Red = 16,
    White = 32
}


public static class Symbol
{
    private static SortedList<Color, string>? _colors;

    public static IReadOnlyDictionary<Color, string> Colors =>
        _colors ??= new()
        {
            [Color.Black] = "B",
            [Color.Blue] = "U",
            [Color.Green] = "G",
            [Color.Red] = "R",
            [Color.White] = "W"
        };
}


[Owned]
public class Flip
{
    [Display(Name = "Multiverse Id")]
    public string MultiverseId { get; init; } = default!;

    [Display(Name = "Mana Cost")]
    public string? ManaCost { get; init; } = default!;

    [Display(Name = "Converted Mana Cost")]
    [Range(0f, 1_000_000f)]
    public float? Cmc { get; init; }

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
