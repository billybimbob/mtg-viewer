using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    public class Suggestion
    {
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [Display(Name = "Sent To")]
        [JsonIgnore]
        public UserRef Receiver { get; init; } = null!;
        public string ReceiverId { get; init; } = null!;


        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck? To { get; init; }
        public int? ToId { get; init; }


        [StringLength(80)]
        public string? Comment { get; set; }

        [Display(Name = "Sent At")]
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
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck From { get; init; } = null!;
        public int FromId { get; init; }


        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck To { get; init; } = null!;
        public int ToId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }
}