using System.Collections.Generic;

namespace MTGViewer.Models
{
    public class User
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public IList<Location> Decks { get; set; }

        public string Name
        {
            get => FirstName + " " + LastName;
        }
    }
}