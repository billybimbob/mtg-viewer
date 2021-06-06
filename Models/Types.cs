using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Models
{

    public class Name
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }

    public class Color
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }

    public class SuperType
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }

    public class Type
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }

    public class SubType
    {
        public int Id { get; set; }
        public string Value { get; set; }

        [Required]
        public Card Card { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }
}