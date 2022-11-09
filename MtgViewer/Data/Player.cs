using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Data;

public class Player
{
    [JsonConstructor]
    public Player()
    {
    }

    [SetsRequiredMembers]
    public Player(CardUser user)
    {
        Id = user.Id;
        Name = user.DisplayName;
    }

    public required string Id { get; init; }

    [StringLength(256, MinimumLength = 1)]
    public required string Name { get; set; }

    public bool ResetRequested { get; set; }

    [JsonIgnore]
    public List<Deck> Decks { get; init; } = new();
}
