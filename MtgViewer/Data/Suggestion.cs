using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(CardId), nameof(ReceiverId), nameof(ToId), IsUnique = true)]
public class Suggestion
{
    [JsonIgnore]
    public int Id { get; private set; }

    [JsonIgnore]
    public Card Card { get; init; } = default!;
    public string CardId { get; init; } = default!;

    [JsonIgnore]
    public string ReceiverId { get; init; } = default!;

    [Display(Name = "Sent To")]
    public Player Receiver { get; init; } = default!;

    [JsonIgnore]
    public int? ToId { get; init; }

    [Display(Name = "To Deck")]
    public Deck? To { get; init; }

    [Display(Name = "Sent At")]
    public DateTime SentAt { get; private set; }

    [StringLength(80)]
    public string? Comment { get; set; }
}
