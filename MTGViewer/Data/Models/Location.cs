using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;
using MTGViewer.Services;

#nullable enable

namespace MTGViewer.Data
{
    public abstract class Location : Concurrent
    {
        [JsonInclude]
        public int Id { get; protected set; }

        [JsonIgnore]
        internal Discriminator Type { get; private set; }

        [Required]
        [StringLength(20)]
        public string Name { get; set; } = string.Empty;

        [JsonIgnore]
        public List<CardAmount> Cards { get; } = new();
    }


    public class Unclaimed : Location
    {
        public static explicit operator Unclaimed(Deck deck)
        {
            var unclaimed = new Unclaimed { Name = deck.Name };

            unclaimed.Cards.AddRange(deck.Cards);

            return unclaimed;
        }
    }


    public class Deck : Location
    {
        public Deck()
        { }

        public Deck(Unclaimed unclaimed, UserRef owner)
        {
            Id = unclaimed.Id;
            Name = unclaimed.Name;
            Owner = owner;

            Cards.AddRange(unclaimed.Cards);
        }


        [JsonIgnore]
        public UserRef Owner { get; init; } = null!;
        public string OwnerId { get; init; } = null!;


        [JsonIgnore]
        public List<Want> Wants { get; } = new();

        [JsonIgnore]
        [Display(Name = "Give Backs")]
        public List<GiveBack> GiveBacks { get; } = new();


        [JsonIgnore]
        [Display(Name = "Trades To")]
        public List<Trade> TradesTo { get; } = new();

        [JsonIgnore]
        [Display(Name = "Trades From")]
        public List<Trade> TradesFrom { get; } = new();


        public string Colors { get; private set; } = string.Empty;

        public void UpdateColors(CardText toCardText)
        {
            var cardMana = Cards
                .Select(ca => ca.Card.ManaCost)
                .SelectMany( toCardText.FindMana )
                .SelectMany(mana => mana.Value.Split('/'));

            var wantMana = Wants
                .Select(w => w.Card.ManaCost)
                .SelectMany( toCardText.FindMana )
                .SelectMany(mana => mana.Value.Split('/'));

            var colorSymbols = Color.Symbols.Values
                .Intersect( cardMana.Union(wantMana) )
                .Select( toCardText.ManaString );

            Colors = string.Join(string.Empty, colorSymbols);
        }
    }


    public class Box : Location
    {
        [JsonIgnore]
        public Bin Bin { get; set; } = null!;
        public int BinId { get; init; }

        [Range(10, 1000)]
        public int Capacity { get; init; }

        [StringLength(20)]
        public string? Appearance { get; init; }
    }


    public class Bin
    {
        [JsonInclude]
        public int Id { get; private set; }

        [StringLength(10)]
        public string Name { get; init; } = string.Empty;

        [JsonIgnore]
        public List<Box> Boxes { get; } = new();
    }
}