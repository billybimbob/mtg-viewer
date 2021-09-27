using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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


        [MaxLength(80)]
        public string? Comment { get; set; }
    }
    


    [Index(
        nameof(ToId),
        nameof(FromId),
        nameof(CardId), IsUnique = true)]
    public class Trade
    {
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck To { get; init; } = null!;
        public int ToId { get; init; }


        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck From { get; init; } = null!;
        public int FromId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }
}