using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

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

        public IOrderedEnumerable<Color> GetColors() => Cards
            .SelectMany(ca => ca.Card.Colors)
            .Distinct(new EntityComparer<Color>(c => c.Name))
            .OrderBy(c => c.Name);
    }


    public class CardAmount : Concurrent
    {
        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;

        public int LocationId { get; set; }
        public Location Location { get; set; } = null!;

        public bool IsRequest { get; set; }

        [Range(0, int.MaxValue)]
        public int Amount { get; set; }
    }


    public class Trade
    {
        public int Id { get; set; }

        public string CardId { get; set; } = null!;

        public Card Card { get; set; } = null!;

        public CardUser SrcUser { get; set; } = null!;

        public CardUser DestUser { get; set; } = null!;

        public Location? SrcLocation { get; set; }

        public Location DestLocation { get; set; } = null!;

        [Range(0, int.MaxValue)]
        public int Amount { get; set; }

        /// <remarks>
        /// Specifies the trade "flow" with false meaning src to dest,
        /// and true meaning dest to src
        /// </remarks>
        public bool IsCounter { get; set; }

        public bool IsSuggestion => SrcLocation == null;
    }

}