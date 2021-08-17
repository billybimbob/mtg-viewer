using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    public class Transfer : Concurrent
    {
        protected Transfer()
        { }

        public int Id { get; set; }

        [JsonIgnore]
        public Discriminator Type { get; set; }


        [JsonIgnore]
        public Card Card { get; set; } = null!;
        public string CardId { get; set; } = null!;


        [JsonIgnore]
        public CardUser Proposer { get; set; } = null!;
        public string ProposerId { get; set; } = null!;


        [JsonIgnore]
        public CardUser Receiver { get; set; } = null!;
        public string ReceiverId { get; set; } = null!;


        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck To { get; set; } = null!;
        public int ToId { get; set; }


        public ICollection<Deck> Decks { get; set; } = new HashSet<Deck>();


        public bool IsInvolved(string userId) =>
            ReceiverId == userId || ProposerId == userId;


        public CardUser? GetOtherUser(string userId) => userId switch
        {
            _ when userId == ProposerId => Receiver,
            _ when userId == ReceiverId => Proposer,
            _ => null
        };
    }


    public class Suggestion : Transfer
    { }


    public class Trade : Transfer
    {
        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck From { get; set; } = null!;
        public int FromId { get; set; }


        /// <remarks>
        /// Specifies the trade pending, with false pending for the receiver
        /// and true pending for the proposer
        /// </remarks>
        public bool IsCounter { get; set; }

        [Range(0, int.MaxValue)]
        public int Amount { get; set; }


        private Deck? _target;

        [JsonIgnore]
        public Deck? TargetDeck 
        {
            get => ReceiverId switch
            {
                _ when _target != null => _target,
                _ when ReceiverId == To?.OwnerId => To,
                _ when ReceiverId == From?.OwnerId => From,
                _ => null
            };
            private set =>
                _target = value;
        }


        public bool IsWaitingOn(string userId) =>
            ReceiverId == userId && !IsCounter
                || ProposerId == userId && IsCounter;
    }
}