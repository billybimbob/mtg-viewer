using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

#nullable enable

namespace MTGViewer.Data
{
    public class Transfer : Concurrent
    {
        protected Transfer()
        { }

        public int Id { get; set; }

        [JsonIgnore]
        internal Discriminator Type { get; set; }


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
        public Deck? To { get; set; }
        public int? ToId { get; set; }

        public ICollection<Deck> Decks { get; set; } = new HashSet<Deck>();


        public bool IsInvolved(string userId) =>
            ReceiverId == userId || ProposerId == userId;


        public CardUser? GetOtherUser(string userId) => this switch
        {
            _ when userId == ProposerId => Receiver,
            _ when userId == ReceiverId => Proposer,
            _ => null
        };
    }


    public class Suggestion : Transfer
    {
        [MaxLength(80)]
        public string? Comment { get; set; }
    }


    public class Trade : Transfer
    {

        [Display(Name = "To Deck")]
        [JsonIgnore]
        public new Deck To
        {
            get => base.To ?? null!;
            set => base.To = value;
        }
        public new int ToId
        {
            get => base.ToId ?? default;
            set => base.ToId = value;
        }


        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck From { get; set; } = null!;
        public int FromId { get; set; }


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }


        private Deck? _target;

        [JsonIgnore]
        public Deck? TargetDeck 
        {
            get => _target switch
            {
                not null => _target,
                _ when ReceiverId == To?.OwnerId => To,
                _ when ReceiverId == From?.OwnerId => From,
                _ => null
            };
            private set =>
                _target = value;
        }
    }
}