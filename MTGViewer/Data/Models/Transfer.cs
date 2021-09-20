using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

#nullable enable

namespace MTGViewer.Data
{
    public class Transfer : Concurrent
    {
        protected Transfer()
        { }

        [JsonRequired]
        public int Id { get; private set; }

        [JsonIgnore]
        internal Discriminator Type { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [Display(Name = "Offered By")]
        [JsonIgnore]
        public UserRef Proposer { get; init; } = null!;
        public string ProposerId { get; init; } = null!;


        [Display(Name = "Sent To")]
        [JsonIgnore]
        public UserRef Receiver { get; init; } = null!;
        public string ReceiverId { get; init; } = null!;


        public bool IsInvolved(string userId) =>
            ReceiverId == userId || ProposerId == userId;


        public UserRef? GetOtherUser(string userId) => this switch
        {
            _ when userId == ProposerId => Receiver,
            _ when userId == ReceiverId => Proposer,
            _ => null
        };
    }


    public class Suggestion : Transfer
    {
        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck? To { get; init; }
        public int? ToId { get; init; }


        [MaxLength(80)]
        public string? Comment { get; set; }
    }


    public class Trade : Transfer
    {
        [Display(Name = "To Deck")]
        [JsonIgnore]
        public Deck To { get; init; } = null!;
        public int ToId { get; init; }


        [Display(Name = "From Deck")]
        [JsonIgnore]
        public Deck From { get; init; } = null!;
        public int FromId { get; init; }


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
            private init =>
                _target = value;
        }
    }
}