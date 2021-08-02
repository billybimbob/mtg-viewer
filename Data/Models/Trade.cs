using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;

#nullable enable


namespace MTGViewer.Data
{
    [Index(
        nameof(CardId),
        nameof(ProposerId),
        nameof(ReceiverId), 
        nameof(ToId), 
        nameof(FromId), IsUnique = true)]
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


        public int ToId { get; set; }

        [Display(Name = "To Deck")]
        public Location To { get; set; } = null!;


        public int? FromId { get; set; }

        [Display(Name = "From Deck")]
        public CardAmount? From { get; set; }


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }


        public bool IsSuggestion => FromId == default;

        public Location? TargetLocation =>
            ProposerId == To.OwnerId
                ? From?.Location
                : To;


        public bool IsInvolved(string userId) =>
            !IsSuggestion
                && (ReceiverId == userId || ProposerId == userId);


        public bool IsWaitingOn(string userId) =>
            !IsSuggestion
                && (ReceiverId == userId && !IsCounter
                    || ProposerId == userId && IsCounter);


        public CardUser GetOtherUser(string userId) =>
            ProposerId != userId ? Proposer : Receiver;


        public IEnumerable<Location> GetLocations()
        {
            yield return To;

            if (!IsSuggestion && From is not null)
            {
                yield return From.Location;
            }
        }
    }
}