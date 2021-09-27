using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(LocationId),
        nameof(CardId), IsUnique = true)]
    public class CardAmount : Concurrent
    {
        [JsonRequired]
        public int Id { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [JsonIgnore]
        public Location Location { get; init; } = null!;
        public int LocationId { get; init; }


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }
    }
}