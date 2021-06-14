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
        public int Id { get; set; }
        public string Name { get; set; }

        public IList<Card> Cards { get; set; }
    }

    public class SuperType
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public IList<Card> Cards { get; set; }
    }

    public class Type
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public IList<Card> Cards { get; set; }
    }

    public class SubType
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public IList<Card> Cards { get; set; }
    }
}