using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

#nullable enable

namespace MTGViewer.Data
{
    public class Location : Concurrent
    {
        protected Location()
        { }


        [JsonRequired]
        public int Id { get; private set; }

        [Required]
        [StringLength(20)]
        public string Name { get; set; } = null!;

        [JsonIgnore]
        internal Discriminator Type { get; private set; }

        [JsonIgnore]
        public List<CardAmount> Cards { get; } = new();
    }


    public class Deck : Location
    {
        [JsonIgnore]
        public UserRef Owner { get; init; } = null!;
        public string OwnerId { get; init; } = null!;

        public string AllColorSymbols { get; private set; } = null!;

        [JsonIgnore]
        public List<CardRequest> Wants { get; } = new();

        [JsonIgnore]
        public List<Trade> TradesTo { get; } = new();

        [JsonIgnore]
        public List<Trade> TradesFrom { get; } = new();


        public IEnumerable<string> GetColorSymbols() => AllColorSymbols.Split(',');

        public void UpdateColorSymbols()
        {
            var cardSymbols = Cards
                .SelectMany(ca => ca.Card.GetManaSymbols());

            var requestSymbols = Wants
                .SelectMany(cr => cr.Card.GetManaSymbols());

            var allSymbols = cardSymbols.Union(requestSymbols);
            var colorSymbols = Color.COLORS.Values.Intersect(allSymbols);

            AllColorSymbols = string.Join(',', colorSymbols);
        }
    }


    public class Box : Location
    {
        [JsonIgnore]
        public Bin Bin { get; set; } = null!;
        public int BinId { get; init; }


        [StringLength(10)]
        public string? Color { get; init; }
    }


    public class Bin
    {
        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
        [StringLength(10)]
        public string Name { get; init; } = null!;

        [JsonIgnore]
        public List<Box> Boxes { get; } = new();
    }
}