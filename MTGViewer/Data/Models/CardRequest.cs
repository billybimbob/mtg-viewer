using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(CardId),
        nameof(TargetId),
        nameof(IsReturn), IsUnique = true)]
    public class CardRequest : Concurrent
    {
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [JsonIgnore]
        public Deck Target { get; init; } = null!;
        public int TargetId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }

        public bool IsReturn { get; init; }
    }
}