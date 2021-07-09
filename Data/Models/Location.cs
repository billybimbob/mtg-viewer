using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;

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


    [Index(nameof(LocationId),nameof(CardId), nameof(IsRequest), IsUnique = true)]
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


    public class Trade
    {
        public int Id { get; set; }

        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;


        [Display(Name = "From User")]
        public CardUser FromUser { get; set; } = null!;

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


    public static class TradeFilter
    {
        public static Expression<Func<Trade, bool>> PendingFor(string userId) =>
            trade =>
                trade.FromId != default
                    && (trade.ToUser.Id == userId && !trade.IsCounter
                        || trade.FromUser.Id == userId && trade.IsCounter);

        public static Expression<Func<Trade, bool>> PendingFor(int deckId) =>
            trade =>
                trade.ToId == deckId && !trade.IsCounter
                    || trade.FromId == deckId && trade.IsCounter;

        public static Expression<Func<Trade, bool>> Suggestion =>
            trade =>
                trade.FromId == default;

        public static Expression<Func<Trade, bool>> SuggestionFor(string userId) =>
            trade =>
                trade.From == default
                    && trade.ToUser.Id ==userId;
    }

}