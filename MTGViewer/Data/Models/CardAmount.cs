using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

#nullable enable

namespace MTGViewer.Data
{
    [Index(
        nameof(LocationId),
        nameof(CardId), IsUnique = true)]
    public class CardAmount : Concurrent
    {
        [JsonRequired]
        public int Id { get; private set; }

        // [JsonIgnore]
        // internal Discriminator Type { get; private set; }

        [JsonIgnore]
        public Card Card { get; init; } = null!;
        public string CardId { get; init; } = null!;


        [JsonIgnore]
        public Location Location { get; init; } = null!;
        public int LocationId { get; init; }


        [Range(0, int.MaxValue)]
        public int Amount { get; set; }
    }


    // public class BoxAmount : CardAmount
    // {
    //     public BoxAmount() : base()
    //     { }

    //     [JsonIgnore]
    //     public Box Box
    //     {
    //         get => (Location as Box)!;
    //         init => Location = value;
    //     }
    // }


    // [Index(
    //     nameof(LocationId),
    //     nameof(CardId),
    //     nameof(Intent), IsUnique = true)]
    // public class DeckAmount : CardAmount
    // {
    //     public DeckAmount() : base()
    //     { }

    //     [JsonIgnore]
    //     public Deck Deck
    //     {
    //         get => (Location as Deck)!;
    //         init => Location = value;
    //     }

    //     public Intent Intent { get; init; }
    // }
}