using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

public abstract class Theorycraft : Location
{
    public Color Color { get; set; }

    public List<Want> Wants { get; init; } = new();
}

[Index(nameof(OwnerId), nameof(Type), nameof(Id))]
public class Deck : Theorycraft
{
    [JsonIgnore]
    public string OwnerId { get; init; } = default!;
    public Player Owner { get; init; } = default!;

    public List<Giveback> Givebacks { get; init; } = new();

    [Display(Name = "Trades To")]
    public List<Trade> TradesTo { get; init; } = new();

    [Display(Name = "Trades From")]
    public List<Trade> TradesFrom { get; init; } = new();
}

public class Unclaimed : Theorycraft
{
    public static explicit operator Unclaimed(Deck deck)
    {
        var unclaimed = new Unclaimed { Name = deck.Name };

        unclaimed.Holds.AddRange(deck.Holds);
        unclaimed.Wants.AddRange(deck.Wants);

        return unclaimed;
    }
}
