using System;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Services.Search.Database;

[Keyless]
[Table("cardIdentifiers")]
[Index("Uuid", Name = "cardIdentifiers_uuid")]
public partial class CardIdentifier
{
    [Column("cardKingdomId")]
    public string? CardKingdomId { get; set; }

    [Column("cardsphereId")]
    public string? CardsphereId { get; set; }

    [Column("mcmId")]
    public string? McmId { get; set; }

    [Column("mcmMetaId")]
    public string? McmMetaId { get; set; }

    [Column("mtgArenaId")]
    public string? MtgArenaId { get; set; }

    [Column("mtgoFoilId")]
    public string? MtgoFoilId { get; set; }

    [Column("mtgoId")]
    public string? MtgoId { get; set; }

    [Column("multiverseId")]
    public string? MultiverseId { get; set; }

    [Column("scryfallId")]
    public string? ScryfallId { get; set; }

    [Column("tcgplayerProductId")]
    public string? TcgplayerProductId { get; set; }

    [Column("uuid")]
    [ForeignKey(nameof(Card.Uuid))]
    public string Uuid { get; set; } = string.Empty;
}
