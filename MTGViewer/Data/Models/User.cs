using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

using MTGViewer.Areas.Identity.Data;

#nullable enable

namespace MTGViewer.Data
{
    public class UserRef
    {
        [JsonConstructor]
        private UserRef() // should not be used
        {
            Id = null!;
            Name = null!;
        }

        public UserRef(CardUser user)
        {
            Id = user.Id;
            Name = user.Name;
        }

        [Key]
        [JsonProperty]
        public string Id { get; private set; }

        [JsonProperty]
        public string Name { get; private set; }

        [JsonIgnore]
        public ICollection<Deck> Decks { get; } = new HashSet<Deck>();
    }
}