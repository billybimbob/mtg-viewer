using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Data.Concurrency;

namespace MTGViewer.Data;

public class Suggestion
{
    [JsonIgnore]
    public int Id { get; private set; }


    [JsonIgnore]
    public string CardId { get; init; } = null!;
    public Card Card { get; init; } = null!;


    [JsonIgnore]
    public string ReceiverId { get; init; } = null!;

    [Display(Name = "Sent To")]
    public UserRef Receiver { get; init; } = null!;


    [JsonIgnore]
    public int? ToId { get; init; }

    [Display(Name = "To Deck")]
    public Deck? To { get; init; }


    [StringLength(80)]
    public string? Comment { get; set; }

    [Display(Name = "Sent At")]
    [JsonInclude]
    public DateTime SentAt { get; private set; }
}



/// <remarks>
/// Makes the assumption that trades are always initiated 
/// by the owner of the To deck, and the owner of the 
/// From deck accepts or denies the trade
/// </remarks>
[Index(
    nameof(FromId),
    nameof(ToId),
    nameof(CardId), IsUnique = true)]
public class Trade : Concurrent
{
    [JsonIgnore]
    public int Id { get; private set; }


    [JsonIgnore]
    public string CardId { get; init; } = null!;
    public Card Card { get; init; } = null!;


    [JsonIgnore]
    public int FromId { get; init; }

    [Display(Name = "From Deck")]
    public Deck From { get; init; } = null!;


    [JsonIgnore]
    public int ToId { get; init; }

    [Display(Name = "To Deck")]
    public Deck To { get; init; } = null!;


    [Range(1, int.MaxValue)]
    public int Amount { get; set; }
}