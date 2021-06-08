using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Models
{
    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public IList<Card> Cards { get; set; }

        public string Name
        {
            get => FirstName + " " + LastName;
        }
    }
}