using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Data
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

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class SuperType
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class Type
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }

    public class SubType
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public Card Card { get; set; }
    }
}