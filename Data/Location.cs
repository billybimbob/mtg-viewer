using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Data
{
    public class Location
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Key]
        public CardUser Owner { get; set; }

        public IList<CardAmount> Cards { get; set; }
    }

    public class CardAmount
    {
        public int Id { get; set; }

        [Key]
        public Card Card { get; set; }

        [Key]
        public Location Location { get; set; }

        public bool IsRequest { get; set; }

        [Range(1, int.MaxValue)]
        public int Amount { get; set; }
    }


}