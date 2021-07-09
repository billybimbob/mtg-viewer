using System;
using System.Linq;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

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


    [Index(
        nameof(CardId),
        nameof(FromUserId),
        nameof(ToUserId),
        nameof(FromId),
        nameof(ToId), IsUnique = true)]
    public class Trade
    {
        public int Id { get; set; }

        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;


        public string FromUserId { get; set; } = null!;

        [Display(Name = "From User")]
        public CardUser FromUser { get; set; } = null!;

        public string ToUserId { get; set; } = null!;

        [Display(Name = "To User")]
        public CardUser ToUser { get; set; } = null!;


        public int? FromId { get; set; }

        [Display(Name = "From Deck")]
        public CardAmount? From { get; set; }


        public int ToId { get; set; }

        [Display(Name = "To Deck")]
        public Location To { get; set; } = null!;


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }

        /// <remarks>
        /// Specifies the trade pending, with false meaning pending for dest,
        /// and true meaning the pending on src
        /// </remarks>
        public bool IsCounter { get; set; }

        public bool IsSuggestion => FromId == default;
    }
}