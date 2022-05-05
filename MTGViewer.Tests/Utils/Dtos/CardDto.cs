using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MtgApiManager.Lib.Model;

namespace MTGViewer.Tests.Utils.Dtos;

internal class CardDto
{
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("border")]
    public string Border { get; set; } = string.Empty;

    [JsonPropertyName("cmc")]
    public float? Cmc { get; set; }

    [JsonPropertyName("colorIdentity")]
    public string[] ColorIdentity { get; set; } = Array.Empty<string>();

    [JsonPropertyName("colors")]
    public string[] Colors { get; set; } = Array.Empty<string>();

    [JsonPropertyName("flavor")]
    public string? Flavor { get; set; }

    [JsonPropertyName("foreignNames")]
    public ForeignNameDto[] ForeignNames { get; set; } = Array.Empty<ForeignNameDto>();

    [JsonPropertyName("hand")]
    public string? Hand { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("layout")]
    public string Layout { get; set; } = string.Empty;

    [JsonPropertyName("legalities")]
    public LegalityDto[] Legalities { get; set; } = Array.Empty<LegalityDto>();

    [JsonPropertyName("life")]
    public string? Life { get; set; }

    [JsonPropertyName("loyalty")]
    public string? Loyalty { get; set; }

    [JsonPropertyName("manaCost")]
    public string? ManaCost { get; set; }

    [JsonPropertyName("multiverseid")]
    public string? MultiverseId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("names")]
    public string[] Names { get; set; } = Array.Empty<string>();

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("originalText")]
    public string? OriginalText { get; set; }

    [JsonPropertyName("originalType")]
    public string? OriginalType { get; set; }

    [JsonPropertyName("power")]
    public string? Power { get; set; }

    [JsonPropertyName("printings")]
    public string[] Printings { get; set; } = Array.Empty<string>();

    [JsonPropertyName("rarity")]
    public string? Rarity { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("reserved")]
    public bool? Reserved { get; set; }

    [JsonPropertyName("rulings")]
    public RulingDto[] Rulings { get; set; } = Array.Empty<RulingDto>();

    [JsonPropertyName("set")]
    public string Set { get; set; } = string.Empty;

    [JsonPropertyName("setName")]
    public string SetName { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("starter")]
    public bool? Starter { get; set; }

    [JsonPropertyName("subtypes")]
    public string[] SubTypes { get; set; } = Array.Empty<string>();

    [JsonPropertyName("supertypes")]
    public string[] SuperTypes { get; set; } = Array.Empty<string>();

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("timeshifted")]
    public bool? Timeshifted { get; set; }

    [JsonPropertyName("toughness")]
    public string? Toughness { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("types")]
    public string[] Types { get; set; } = Array.Empty<string>();

    [JsonPropertyName("variations")]
    public string[] Variations { get; set; } = Array.Empty<string>();

    [JsonPropertyName("watermark")]
    public string? Watermark { get; set; }
}

internal class ApiCard : ICard
{
    public ApiCard(CardDto card)
    {
        Artist = card.Artist;
        Border = card.Border;
        Cmc = card.Cmc;

        ColorIdentity = card.ColorIdentity;
        Colors = card.Colors;
        Flavor = card.Flavor;

        ForeignNames = card.ForeignNames?.Cast<IForeignName>().ToList();
        Hand = card.Hand;
        Id = card.Id;

        ImageUrl = card.ImageUrl is string url ? new Uri(url) : null;
        Layout = card.Layout;
        Legalities = card.Legalities?.Cast<ILegality>().ToList();

        Life = card.Life;
        Loyalty = card.Loyalty;
        ManaCost = card.ManaCost;
        MultiverseId = card.MultiverseId;

        Name = card.Name;
        Names = card.Names;
        Number = card.Number;

        OriginalText = card.OriginalText;
        OriginalType = card.OriginalType;
        Power = card.Power;

        Printings = card.Printings;
        Rarity = card.Rarity;
        ReleaseDate = card.ReleaseDate;

        Reserved = card.Reserved;
        Rulings = card.Rulings?.OfType<IRuling>().ToList();
        Set = card.Set;

        SetName = card.SetName;
        Source = card.Source;
        Starter = card.Starter;

        SubTypes = card.SubTypes;
        SuperTypes = card.SuperTypes;
        Text = card.Text;

        Timeshifted = card.Timeshifted;
        Toughness = card.Toughness;
        Type = card.Type;

        Types = card.Types;
        Variations = card.Variations;
        Watermark = card.Watermark;
    }

    public string Artist { get; set; } = string.Empty;

    public string Border { get; set; } = string.Empty;

    public float? Cmc { get; set; }

    public string[] ColorIdentity { get; set; } = Array.Empty<string>();

    public string[] Colors { get; set; } = Array.Empty<string>();

    public string? Flavor { get; set; }

    public List<IForeignName>? ForeignNames { get; set; }

    public string? Hand { get; set; }

    public string Id { get; set; } = string.Empty;

    public Uri? ImageUrl { get; set; }

    public bool IsMultiColor => ColorIdentity.Length > 1;

    public bool IsColorless => ColorIdentity.Length == 0;

    public string Layout { get; set; } = string.Empty;

    public List<ILegality>? Legalities { get; set; }

    public string? Life { get; set; }

    public string? Loyalty { get; set; }

    public string? ManaCost { get; set; }

    public string? MultiverseId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string[] Names { get; set; } = Array.Empty<string>();

    public string? Number { get; set; }

    public string? OriginalText { get; set; }

    public string? OriginalType { get; set; }

    public string? Power { get; set; }

    public string[] Printings { get; set; } = Array.Empty<string>();

    public string? Rarity { get; set; }

    public string? ReleaseDate { get; set; }

    public bool? Reserved { get; set; }

    public List<IRuling>? Rulings { get; set; }

    public string Set { get; set; } = string.Empty;

    public string SetName { get; set; } = string.Empty;

    public string? Source { get; set; }

    public bool? Starter { get; set; }

    public string[] SubTypes { get; set; } = Array.Empty<string>();

    public string[] SuperTypes { get; set; } = Array.Empty<string>();

    public string? Text { get; set; }

    public bool? Timeshifted { get; set; }

    public string? Toughness { get; set; }

    public string Type { get; set; } = string.Empty;

    public string[] Types { get; set; } = Array.Empty<string>();

    public string[] Variations { get; set; } = Array.Empty<string>();

    public string? Watermark { get; set; }
}
