using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(FromId),
        nameof(ToId),
        nameof(CardId),
        nameof(TransactionId), IsUnique = true)]
    public class Change
    {
        [JsonInclude]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [JsonIgnore]
        public Location? From { get; init; } = null!;
        public int? FromId { get; init; }


        [JsonIgnore]
        public Location To { get; init; } = null!;
        public int ToId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }


        [JsonIgnore]
        public Transaction Transaction { get; init; } = null!;
        public int TransactionId { get; init; }
    }


    public class Transaction
    {
        [JsonInclude]
        public int Id { get; private set; }

        [Display(Name = "Applied At")]
        public DateTime AppliedAt { get; private set; }

        [JsonIgnore]
        public List<Change> Changes { get; } = new();
    }
}