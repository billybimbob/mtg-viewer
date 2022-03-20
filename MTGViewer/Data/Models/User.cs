using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Data;

public class UserRef
{
    public UserRef()
    { }

    public UserRef(CardUser user)
    {
        Id = user.Id;
        Name = user.DisplayName;
    }

    [Key]
    public string Id { get; init; } = default!;

    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = default!;

    public bool ResetRequested { get; set; }

    [JsonIgnore]
    public List<Deck> Decks { get; init; } = new();
}
