using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    public class Suggestion : Concurrent
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


        public int ToId { get; set; }

        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck To { get; set; } = null!;


        [JsonIgnore]
        public bool IsSuggestion { get; private set; }


        public bool IsInvolved(string userId) =>
            ReceiverId == userId || ProposerId == userId;


        public CardUser? GetOtherUser(string userId) => userId switch
        {
            _ when userId == ProposerId => Receiver,
            _ when userId == ReceiverId => Proposer,
            _ => null
        };
    }


    public class Trade : Suggestion
    {
        public int FromId { get; set; }

        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck From { get; set; } = null!;


        /// <remarks>
        /// Specifies the trade pending, with false pending for the receiver
        /// and true pending for the proposer
        /// </remarks>
        public bool IsCounter { get; set; }

        [Range(0, int.MaxValue)]
        public int Amount { get; set; }


        [JsonIgnore]
        public Deck? TargetDeck => ReceiverId switch
        {
            _ when ReceiverId == To.OwnerId => To,
            _ when ReceiverId == From.OwnerId => From,
            _ => null
        };


        public bool IsWaitingOn(string userId) =>
            ReceiverId == userId && !IsCounter
                || ProposerId == userId && IsCounter;


        public IEnumerable<Deck> GetDecks()
        {
            yield return To;
            yield return From;
        }
    }
}