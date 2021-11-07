using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(Type),
        nameof(DeckId),
        nameof(CardId), IsUnique = true)]
    public abstract class CardRequest : Concurrent
    {
        [JsonInclude]
        public int Id { get; private set; }

        [JsonIgnore]
        internal Discriminator Type { get; private set; }


        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [JsonIgnore]
        public Deck Deck { get; init; } = null!;
        public int DeckId { get; init; }


        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }


    public class Want : CardRequest
    { }


    public class GiveBack : CardRequest
    { }
}