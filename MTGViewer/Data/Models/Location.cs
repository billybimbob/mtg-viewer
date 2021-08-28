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

        [JsonProperty]
        public int Id { get; private set; }

        public string Name { get; set; }

        [JsonIgnore]
        internal Discriminator Type { get; private set; }

        [JsonIgnore]
        public ICollection<CardAmount> Cards { get; } = new HashSet<CardAmount>();


        public IOrderedEnumerable<Color> GetColors() => Cards
            .SelectMany(ca => ca.Card.Colors)
            .Distinct(new EntityComparer<Color>(c => c.Name))
            .OrderBy(c => c.Name);

        public IOrderedEnumerable<string> GetColorSymbols() => Cards
            .SelectMany(ca => ca.Card.GetManaSymbols())
            .Distinct()
            .Where(s => Color.COLORS.Values.Contains(s))
            .OrderBy(s => s);
    }


    public class Shared : Location
    {
        public Shared(string name) : base(name)
        { }
    }


    public class Deck : Location
    {
        public Deck(string name) : base(name)
        { }

        [JsonIgnore]
        public UserRef Owner { get; init; } = null!;
        public string OwnerId { get; init; } = null!;

        // [JsonIgnore]
        // public ICollection<Transfer> Transfers { get; } = new HashSet<Transfer>();
    }
}