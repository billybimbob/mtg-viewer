using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Data;

public class Player
{
    [JsonConstructor]
    public Player()
    {
    }

    public Player(CardUser user)
    {
        Id = user.Id;
        Name = user.DisplayName;
    }

    public string Id { get; init; } = default!;

    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = default!;

    public bool ResetRequested { get; set; }

    [JsonIgnore]
    public List<Deck> Decks { get; init; } = new();
}
