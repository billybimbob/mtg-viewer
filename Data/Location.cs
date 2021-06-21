using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using MTGViewer.Areas.Identity.Data;


#nullable enable

namespace MTGViewer.Data
{
    public class Location
    {
        public Location(string name)
        {
            Name = name;
        }

        public int Id { get; set; }

        public string Name { get; set; }

        public CardUser Owner { get; set; } = null!;

        public ISet<CardAmount> Cards { get; } = new HashSet<CardAmount>();
    }

    public class CardAmount
    {
        public int Id { get; set; }

        public Card Card { get; set; } = null!;

        public Location? Location { get; set; }

        public bool IsRequest { get; set; }

        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }


}