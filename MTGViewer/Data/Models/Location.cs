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
    }


    public class Deck : Location
    {
        public Deck(string name) : base(name)
        { }

        [JsonIgnore]
        public UserRef Owner { get; init; } = null!;
        public string OwnerId { get; init; } = null!;


        [JsonIgnore]
        public ICollection<DeckAmount> Cards { get; } = new List<DeckAmount>();

        [JsonIgnore]
        public ICollection<Trade> TradesTo { get; } = new List<Trade>();

        [JsonIgnore]
        public ICollection<Trade> TradesFrom { get; } = new List<Trade>();

        [JsonIgnore]
        public ICollection<Suggestion> Suggestions { get; } = new List<Suggestion>();


        public IOrderedEnumerable<Color> GetColors() => Cards.GetColors();

        public IOrderedEnumerable<string> GetColorSymbols() => Cards.GetColorSymbols();
    }


    public class Box : Location
    {
        public Box(string name) : base(name)
        { }

        [JsonIgnore]
        public Bin Bin { get; set; } = null!;
        public int BinId { get; init; }

        public string? Color { get; init; }

        [JsonIgnore]
        public ICollection<BoxAmount> Cards { get; } = new List<BoxAmount>();


        public IOrderedEnumerable<Color> GetColors() => Cards.GetColors();

        public IOrderedEnumerable<string> GetColorSymbols() => Cards.GetColorSymbols();
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


    internal static class AmountColors
    {
        internal static IOrderedEnumerable<Color> GetColors(
            this IEnumerable<CardAmount> amounts)
        {
            return amounts
                .SelectMany(ca => ca.Card.Colors)
                .Distinct(new EntityComparer<Color>(c => c.Name))
                .OrderBy(c => c.Name);
        }

        internal static IOrderedEnumerable<string> GetColorSymbols(
            this IEnumerable<CardAmount> amounts)
        {
            return amounts
                .SelectMany(ca => ca.Card.GetManaSymbols())
                .Distinct()
                .Intersect(Data.Color.COLORS.Values)
                .OrderBy(s => s);
        }
    }
}