using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

/// <remarks>
/// Makes the assumption that trades are always initiated
/// by the owner of the To deck, and the owner of the
/// From deck accepts or denies the trade
/// </remarks>
[Index(nameof(ToId), nameof(FromId), nameof(CardId), IsUnique = true)]
public class Trade : Concurrent
{
    [JsonIgnore]
    public int Id { get; private set; }

    [JsonIgnore]
    public Card Card { get; init; } = default!;
    public string CardId { get; init; } = default!;

    [JsonIgnore]
    public int ToId { get; init; }

    [Display(Name = "To Deck")]
    public Deck To { get; init; } = default!;

    [JsonIgnore]
    public int FromId { get; init; }

    [Display(Name = "From Deck")]
    public Deck From { get; init; } = default!;

    [Range(1, 4_096)]
    public int Copies { get; set; }
}
