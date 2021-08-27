using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using MTGViewer.Areas.Identity.Data;

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

        public int Id { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        internal Discriminator Type { get; set; }

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
        public CardUser Owner { get; set; } = null!;
        public string OwnerId { get; set; } = null!;

        [JsonIgnore]
        public ICollection<Transfer> Transfers { get; } = new HashSet<Transfer>();
    }
}