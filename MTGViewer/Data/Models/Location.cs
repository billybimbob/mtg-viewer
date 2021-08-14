using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data.Concurrency;

#nullable enable

namespace MTGViewer.Data
{
    public class Location : Concurrent
    {
        public Location(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        [JsonIgnore]
        public bool IsShared { get; private set; }

        [JsonIgnore]
        public ICollection<CardAmount> Cards { get; } = new HashSet<CardAmount>();

        public IOrderedEnumerable<Color> GetColors() => Cards
            .SelectMany(ca => ca.Card.Colors)
            .Distinct(new EntityComparer<Color>(c => c.Name))
            .OrderBy(c => c.Name);
    }


    public class Deck : Location
    {
        public Deck(string name) : base(name)
        { }

        public string OwnerId { get; set; } = null!;

        [JsonIgnore]
        public CardUser Owner { get; set; } = null!;
    }
}