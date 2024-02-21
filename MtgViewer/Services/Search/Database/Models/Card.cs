using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Services.Search.Database;

[Table("cards")]
[Index("Uuid", Name = "cards_uuid")]
public partial class Card
{
    [Key]
    [Column("uuid", TypeName = "VARCHAR(36)")]
    public string Uuid { get; set; } = null!;

    [ForeignKey(nameof(Uuid))]
    public CardIdentifier CardIdentifier { get; set; } = null!;

    [Column("artist")]
    public string? Artist { get; set; }

    [Column("borderColor")]
    public string? BorderColor { get; set; }

    [Column("cardParts")]
    public string? CardParts { get; set; }

    [Column("colorIdentity")]
    public string? ColorIdentity { get; set; }

    [Column("colors")]
    public string? Colors { get; set; }

    [Column("defense")]
    public string? Defense { get; set; }

    [Column("flavorText")]
    public string? FlavorText { get; set; }

    [Column("isFullArt", TypeName = "BOOLEAN")]
    public bool? IsFullArt { get; set; }

    [Column("layout")]
    public string? Layout { get; set; }

    [Column("life")]
    public string? Life { get; set; }

    [Column("loyalty")]
    public string? Loyalty { get; set; }

    [Column("manaCost")]
    public string? ManaCost { get; set; }

    [Column("manaValue", TypeName = "FLOAT")]
    public float? ManaValue { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("number")]
    public string? Number { get; set; }

    [Column("power")]
    public string? Power { get; set; }

    [Column("rarity")]
    public string? Rarity { get; set; }

    [Column("relatedCards")]
    public string? RelatedCards { get; set; }

    [Column("side")]
    public string? Side { get; set; }

    [Column("subtypes")]
    public string? Subtypes { get; set; }

    [Column("supertypes")]
    public string? Supertypes { get; set; }

    [Column("text")]
    public string? Text { get; set; }

    [Column("toughness")]
    public string? Toughness { get; set; }

    [Column("type")]
    public string? Type { get; set; }

    [Column("types")]
    public string? Types { get; set; }

    [Column("setCode")]
    public string SetCode { get; set; } = string.Empty;

    [ForeignKey(nameof(SetCode))]
    public Set Set { get; set; } = null!;
}
