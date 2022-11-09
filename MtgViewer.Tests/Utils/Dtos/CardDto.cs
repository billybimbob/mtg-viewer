using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using MtgApiManager.Lib.Model;

namespace MtgViewer.Tests.Utils.Dtos;

internal class CardDto
{
    [JsonPropertyName("artist")]
    public required string Artist { get; set; }

    [JsonPropertyName("border")]
    public required string Border { get; set; }

    [JsonPropertyName("cmc")]
    public required float? Cmc { get; set; }

    [JsonPropertyName("colorIdentity")]
    public required string[] ColorIdentity { get; set; }

    [JsonPropertyName("colors")]
    public required string[] Colors { get; set; }

    [JsonPropertyName("flavor")]
    public required string? Flavor { get; set; }

    [JsonPropertyName("foreignNames")]
    public required ForeignNameDto[] ForeignNames { get; set; }

    [JsonPropertyName("hand")]
    public required string? Hand { get; set; }

    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("imageUrl")]
    public required string? ImageUrl { get; set; }

    [JsonPropertyName("layout")]
    public required string Layout { get; set; }

    [JsonPropertyName("legalities")]
    public required LegalityDto[] Legalities { get; set; }

    [JsonPropertyName("life")]
    public required string? Life { get; set; }

    [JsonPropertyName("loyalty")]
    public required string? Loyalty { get; set; }

    [JsonPropertyName("manaCost")]
    public required string? ManaCost { get; set; }

    [JsonPropertyName("multiverseid")]
    public required string? MultiverseId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("names")]
    public required string[] Names { get; set; }

    [JsonPropertyName("number")]
    public required string? Number { get; set; }

    [JsonPropertyName("originalText")]
    public required string? OriginalText { get; set; }

    [JsonPropertyName("originalType")]
    public required string? OriginalType { get; set; }

    [JsonPropertyName("power")]
    public required string? Power { get; set; }

    [JsonPropertyName("printings")]
    public required string[] Printings { get; set; }

    [JsonPropertyName("rarity")]
    public required string? Rarity { get; set; }

    [JsonPropertyName("releaseDate")]
    public required string? ReleaseDate { get; set; }

    [JsonPropertyName("reserved")]
    public required bool? Reserved { get; set; }

    [JsonPropertyName("rulings")]
    public required RulingDto[] Rulings { get; set; }

    [JsonPropertyName("set")]
    public required string Set { get; set; }

    [JsonPropertyName("setName")]
    public required string SetName { get; set; }

    [JsonPropertyName("source")]
    public required string? Source { get; set; }

    [JsonPropertyName("starter")]
    public required bool? Starter { get; set; }

    [JsonPropertyName("subtypes")]
    public required string[] SubTypes { get; set; }

    [JsonPropertyName("supertypes")]
    public required string[] SuperTypes { get; set; }

    [JsonPropertyName("text")]
    public required string? Text { get; set; }

    [JsonPropertyName("timeshifted")]
    public required bool? Timeshifted { get; set; }

    [JsonPropertyName("toughness")]
    public required string? Toughness { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("types")]
    public required string[] Types { get; set; }

    [JsonPropertyName("variations")]
    public required string[] Variations { get; set; }

    [JsonPropertyName("watermark")]
    public required string? Watermark { get; set; }
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

    public required string Artist { get; set; }

    public required string Border { get; set; }

    public required float? Cmc { get; set; }

    public required string[] ColorIdentity { get; set; }

    public required string[] Colors { get; set; }

    public required string? Flavor { get; set; }

    public required List<IForeignName>? ForeignNames { get; set; }

    public required string? Hand { get; set; }

    public required string Id { get; set; }

    public required Uri? ImageUrl { get; set; }

    public bool IsMultiColor => ColorIdentity.Length > 1;

    public bool IsColorless => ColorIdentity.Length == 0;

    public required string Layout { get; set; }

    public required List<ILegality>? Legalities { get; set; }

    public required string? Life { get; set; }

    public required string? Loyalty { get; set; }

    public required string? ManaCost { get; set; }

    public required string? MultiverseId { get; set; }

    public required string Name { get; set; }

    public required string[] Names { get; set; }

    public required string? Number { get; set; }

    public required string? OriginalText { get; set; }

    public required string? OriginalType { get; set; }

    public required string? Power { get; set; }

    public required string[] Printings { get; set; }

    public required string? Rarity { get; set; }

    public required string? ReleaseDate { get; set; }

    public required bool? Reserved { get; set; }

    public required List<IRuling>? Rulings { get; set; }

    public required string Set { get; set; }

    public required string SetName { get; set; }

    public required string? Source { get; set; }

    public required bool? Starter { get; set; }

    public required string[] SubTypes { get; set; }

    public required string[] SuperTypes { get; set; }

    public required string? Text { get; set; }

    public required bool? Timeshifted { get; set; }

    public required string? Toughness { get; set; }

    public required string Type { get; set; }

    public required string[] Types { get; set; }

    public required string[] Variations { get; set; }

    public required string? Watermark { get; set; }
}
