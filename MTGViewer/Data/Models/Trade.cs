using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

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

        [JsonIgnore]
        public Card Card { get; set; } = null!;


        public string ProposerId { get; set; } = null!;

        [JsonIgnore]
        public CardUser Proposer { get; set; } = null!;

        public string ReceiverId { get; set; } = null!;

        [JsonIgnore]
        public CardUser Receiver { get; set; } = null!;

        /// <remarks>
        /// Specifies the trade pending, with false pending for the receiver
        /// and true pending for the proposer
        /// </remarks>
        public bool IsCounter { get; set; }


        public int ToId { get; set; }

        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Location To { get; set; } = null!;


        public int? FromId { get; set; }

        [Display(Name = "From Deck")]
        [JsonIgnore]
        public CardAmount? From { get; set; }


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }


        [JsonIgnore]
        public bool IsSuggestion => FromId == default;

        [JsonIgnore]
        public Location? TargetLocation =>
            ProposerId != To.OwnerId
                ? To
                : From?.Location;


        public bool IsInvolved(string userId) =>
            !IsSuggestion
                && (ReceiverId == userId || ProposerId == userId);


        public bool IsWaitingOn(string userId) =>
            !IsSuggestion
                && (ReceiverId == userId && !IsCounter
                    || ProposerId == userId && IsCounter);


        public CardUser? GetOtherUser(string userId) => userId switch
        {
            _ when userId == ProposerId => Receiver,
            _ when userId == ReceiverId => Proposer,
            _ => null
        };


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