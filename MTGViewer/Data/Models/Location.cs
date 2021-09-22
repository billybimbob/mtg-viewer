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
        protected Location(string name)
        {
            Name = name;
        }

        [JsonRequired]
        public int Id { get; private set; }

        public string Name { get; set; }

        [JsonIgnore]
        internal Discriminator Type { get; private set; }


        [JsonIgnore]
        public ICollection<Change> ChangesTo { get; } = new List<Change>();

        [JsonIgnore]
        public ICollection<Change> ChangesFrom { get; } = new List<Change>();

        [JsonIgnore]
        public ICollection<CardAmount> Cards { get; } = new List<CardAmount>();


        public IOrderedEnumerable<Color> GetColors()
        {
            return Cards
                .SelectMany(ca => ca.Card.Colors)
                .Distinct(new EntityComparer<Color>(c => c.Name))
                .OrderBy(c => c.Name);
        }

        public IOrderedEnumerable<string> GetColorSymbols()
        {
            return Cards
                .SelectMany(ca => ca.Card.GetManaSymbols())
                .Distinct()
                .Intersect(Data.Color.COLORS.Values)
                .OrderBy(s => s);
        }
    }


    public class Deck : Location
    {
        public Deck(string name) : base(name)
        { }

        [JsonIgnore]
        public UserRef Owner { get; init; } = null!;
        public string OwnerId { get; init; } = null!;

        [JsonIgnore]
        public ICollection<Suggestion> Suggestions { get; } = new List<Suggestion>();

        [JsonIgnore]
        public ICollection<Exchange> ExchangesTo { get; } = new List<Exchange>();

        [JsonIgnore]
        public ICollection<Exchange> ExchangesFrom { get; } = new List<Exchange>();

        public IEnumerable<Exchange> GetAllExchanges() =>
            ExchangesTo.Concat(ExchangesFrom); // guranteed to be unique between both properties
    }


    public class Box : Location
    {
        public Box(string name) : base(name)
        { }

        [JsonIgnore]
        public Bin Bin { get; set; } = null!;
        public int BinId { get; init; }

        public string? Color { get; init; }
    }


    public class Bin
    {
        public Bin(string name)
        {
            Name = name;
        }

        [JsonRequired]
        public int Id { get; private set; }

        [JsonRequired]
        public string Name { get; private set; }

        [JsonIgnore]
        public ICollection<Box> Boxes = new List<Box>();
    }
}