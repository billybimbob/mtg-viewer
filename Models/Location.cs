using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;


namespace MTGViewer.Models
{
    public class Location
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Key]
        public User Owner { get; set; }

        public IList<CardAmount> Cards { get; set; }
    }

    public class CardAmount
    {
        public int Id { get; set; }

        [Key]
        public Card Card { get; set; }

        [Key]
        public Location Location { get; set; }

        public int Amount { get; set; }
    }


}