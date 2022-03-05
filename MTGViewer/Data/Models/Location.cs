using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;


[Index(nameof(Type), nameof(Id))]
[Index(nameof(Name), nameof(Id))] // could be updated often
public abstract class Location : Concurrent
{
    [Key]
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    internal LocationType Type { get; private set; }

    [StringLength(20, MinimumLength = 1)]
    public string Name { get; set; } = default!;

    public List<Amount> Cards { get; init; } = new();
}


public abstract class Owned : Location
{
    public List<Want> Wants { get; init; } = new();
}


public class Unclaimed : Owned
{
    public static explicit operator Unclaimed(Deck deck)
    {
        var unclaimed = new Unclaimed { Name = deck.Name };

        unclaimed.Cards.AddRange(deck.Cards);
        unclaimed.Wants.AddRange(deck.Wants);

        return unclaimed;
    }
}


[Index(nameof(OwnerId), nameof(Type), nameof(Id))]
public class Deck : Owned
{
    [JsonIgnore]
    public string OwnerId { get; init; } = default!;
    public UserRef Owner { get; init; } = default!;


    [Display(Name = "Give Backs")]
    public List<GiveBack> GiveBacks { get; init; } = new();


    [Display(Name = "Trades To")]
    public List<Trade> TradesTo { get; init; } = new();


    [Display(Name = "Trades From")]
    public List<Trade> TradesFrom { get; init; } = new();


    public Color Color { get; private set; }

    public void UpdateColors()
    {
        var amountColors = Cards
            .Select(a => a.Card.Color);

        var wantColors = Wants
            .Select(w => w.Card.Color);

        Color = amountColors
            .Concat(wantColors)
            .Aggregate(Color.None, (color, card) => color | card);
    }
}


public abstract class Storage : Location
{ }


public class Excess : Storage
{
    public static Excess Create()
    {
        return new Excess
        {
            Name = "Excess",
        };
    }
}


[Index(nameof(BinId), nameof(Type), nameof(Id))]
public class Box : Storage
{
    [JsonIgnore]
    public int BinId { get; init; }
    public Bin Bin { get; set; } = default!;

    [Range(10, 10_000)]
    public int Capacity { get; set; }

    [StringLength(40)]
    public string? Appearance { get; set; }
}


public class Bin
{
    [JsonIgnore]
    public int Id { get; init; }

    [StringLength(10, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public List<Box> Boxes { get; init; } = new();
}
