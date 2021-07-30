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
        nameof(ProposerId),
        nameof(ReceiverId), IsUnique = true)]
    public class Trade : Concurrent
    {
        public int Id { get; set; }

        public string CardId { get; set; } = null!;
        public Card Card { get; set; } = null!;


        public string ProposerId { get; set; } = null!;
        public CardUser Proposer { get; set; } = null!;

        public string ReceiverId { get; set; } = null!;
        public CardUser Receiver { get; set; } = null!;

        /// <remarks>
        /// Specifies the trade pending, with false pending for the receiver
        /// and true pending for the proposer
        /// </remarks>
        public bool IsCounter { get; set; }


        public int? FromId { get; set; }

        [Display(Name = "From Deck")]
        public CardAmount? From { get; set; }


        public int ToId { get; set; }

        [Display(Name = "To Deck")]
        public Location To { get; set; } = null!;


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }

        public bool IsSuggestion => FromId == default;


        public IEnumerable<Location> GetLocations()
        {
            yield return To;

            if (!IsSuggestion && From is not null)
            {
                yield return From.Location;
            }
        }


        public bool IsInvolved(string userId)
        {
            return !IsSuggestion
                && (ReceiverId == userId || ProposerId == userId);
        }


        public bool IsWaitingOn(string userId)
        {
            return !IsSuggestion
                && (ReceiverId == userId && !IsCounter
                    || ProposerId == userId && IsCounter);
        }
    }
}