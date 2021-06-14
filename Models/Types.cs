using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Models
{

    public class Name
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class Color
    {
        [Key]
        public string Name { get; set; }

        public IList<Card> Cards { get; set; }
    }

    public class SuperType
    {
        [Key]
        public string Name { get; set; }
        public IList<Card> Cards { get; set; }
    }

    public class Type
    {
        [Key]
        public string Name { get; set; }
        public IList<Card> Cards { get; set; }
    }

    public class SubType
    {
        [Key]
        public string Name { get; set; }
        public IList<Card> Cards { get; set; }
    }
}