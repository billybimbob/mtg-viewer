using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;


namespace MTGViewer.Models
{
    public class Deck
    {
        public int Id { get; set; }

        [Required]
        public User User { get; set; }

        public string Name { get; set; }

        public IList<CardAmount> Cards { get; set; }
    }

    public class CardAmount
    {
        public int Id { get; set; }

        [Required]
        public Card Card { get; set; }

        [Required]
        public Deck Deck { get; set; }

        public int Amount { get; set; }
    }


}