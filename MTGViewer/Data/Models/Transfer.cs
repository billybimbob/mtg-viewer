using System;
using System.Collections.Generic;
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


        [MaxLength(80)]
        public string? Comment { get; set; }
    }


    [Index(
        nameof(ToId),
        nameof(FromId),
        nameof(CardId), IsUnique = true)]
    public class Exchange : Concurrent
    {
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck? To { get; init; } = null!;
        public int? ToId { get; init; }


        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck? From { get; init; } = null!;
        public int? FromId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }


        private readonly bool _isTrade;

        [JsonIgnore]
        public bool IsTrade
        {
            get => _isTrade
                || (ToId != null || To != null)
                    && (FromId != null || From != null);

            private init => _isTrade = value;
        }
    }


    public class Change
    {
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Location To { get; init; } = null!;
        public int ToId { get; init; }


        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Location From { get; init; } = null!;
        public int FromId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }


        [JsonIgnore]
        public Transaction Transaction { get; init; } = null!;
        public int TransactionId { get; init; }
    }


    public class Transaction
    {
        public int Id { get; private set; }

        public DateTime Applied { get; private set; }

        [JsonIgnore]
        public ICollection<Change> Changes = new List<Change>();
    }
}