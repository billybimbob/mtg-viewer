using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(Type),
        nameof(TargetId),
        nameof(CardId), IsUnique = true)]
    public class CardRequest : Concurrent
    {
        protected CardRequest()
        { }


        [JsonRequired]
        public int Id { get; private set; }

        internal Discriminator Type { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [JsonIgnore]
        public Deck Target { get; init; } = null!;
        public int TargetId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }


    public class Want : CardRequest
    { }


    public class GiveBack : CardRequest
    { }
}