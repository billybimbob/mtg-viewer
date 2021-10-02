using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(ToId),
        nameof(FromId),
        nameof(CardId),
        nameof(TransactionId), IsUnique = true)]
    public class Change
    {
        [JsonRequired]
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
        public int Id { get; private set; }

        public DateTime Applied { get; private set; }

        [JsonIgnore]
        public List<Change> Changes { get; } = new();
    }
}