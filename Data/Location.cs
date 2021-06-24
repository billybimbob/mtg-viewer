using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    public class Location : Concurrent
    {
        public Location(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        public string? OwnerId { get; set; }
        public CardUser? Owner { get; set; }

        public ICollection<CardAmount> Cards { get; } = new HashSet<CardAmount>();

        public bool IsShared => OwnerId == default;
    }

    public class CardAmount : Concurrent
    {
        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;

        public int LocationId { get; set; }
        public Location Location { get; set; } = null!;

        public bool IsRequest { get; set; }

        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }


}