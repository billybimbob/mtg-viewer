using System.Linq;
using System.Collections.Generic;
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

        public string Name { get; set; } = null!;

        [JsonIgnore]
        internal Discriminator Type { get; private set; }

        [JsonIgnore]
        public List<CardAmount> Cards { get; } = new();

        [JsonIgnore]
        public List<Change> ChangesTo { get; } = new();

        [JsonIgnore]
        public List<Change> ChangesFrom { get; } = new();


        public IEnumerable<Change> GetChanges() => ChangesTo.Concat(ChangesFrom);


        public virtual IOrderedEnumerable<string> GetColorSymbols()
        {
            return Cards
                .SelectMany(ca => ca.Card.GetManaSymbols())
                .Distinct()
                .Intersect(Data.Color.COLORS.Values)
                .OrderBy(s => s);
        }

        // public IOrderedEnumerable<Color> GetColors()
        // {
        //     return Cards
        //         .SelectMany(ca => ca.Card.Colors)
        //         .Distinct(new EntityComparer<Color>(c => c.Name))
        //         .OrderBy(c => c.Name);
        // }
    }


    public class Deck : Location
    {
        [JsonIgnore]
        public UserRef Owner { get; init; } = null!;
        public string OwnerId { get; init; } = null!;


        [JsonIgnore]
        public List<CardRequest> Requests { get; } = new();

        [JsonIgnore]
        public List<Trade> TradesTo { get; } = new();

        [JsonIgnore]
        public List<Trade> TradesFrom { get; } = new();


        public IEnumerable<Trade> GetTrades() => TradesTo.Concat(TradesFrom);

        
        public override IOrderedEnumerable<string> GetColorSymbols()
        {
            var cardSymbols = Cards
                .SelectMany(ca => ca.Card.GetManaSymbols());

            var requestSymbols = Requests
                .SelectMany(cr => cr.Card.GetManaSymbols());

            return cardSymbols
                .Union(requestSymbols)
                .Intersect(Data.Color.COLORS.Values)
                .OrderBy(s => s);
        }
    }


    public class Box : Location
    {
        [JsonIgnore]
        public Bin Bin { get; set; } = null!;
        public int BinId { get; init; }

        public string? Color { get; init; }
    }


    public class Bin
    {
        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
        public string Name { get; init; } = null!;

        [JsonIgnore]
        public List<Box> Boxes { get; } = new();
    }
}