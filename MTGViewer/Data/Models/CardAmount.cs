using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Data.Concurrency;

#nullable enable


namespace MTGViewer.Data
{
    [Index(
        nameof(LocationId),
        nameof(CardId),
        nameof(IsRequest), IsUnique = true)]
    public class CardAmount : Concurrent
    {
        public int Id { get; set; }

        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;

        public int LocationId { get; set; }
        public Location Location { get; set; } = null!;

        public bool IsRequest { get; set; }

        [Range(0, int.MaxValue)]
        public int Amount { get; set; }
    }
}