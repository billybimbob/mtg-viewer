using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;
using MTGViewer.Services;

namespace MTGViewer.Data;


public abstract class Location : Concurrent
{
    [Key]
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    internal LocationType Type { get; private set; }

    [StringLength(20)]
    public string Name { get; set; } = string.Empty;

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


[Index(
    nameof(Type),
    nameof(OwnerId))]
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

    public void UpdateColors(CardText toCardText)
    {
        var cardMana = Cards
            .Select(a => a.Card.Color);

        var wantMana = Wants
            .Select(w => w.Card.Color);

        Color = cardMana
            .Concat(wantMana)
            .Aggregate(Color.None, (color, mana) => color | mana);
    }
}


[Index(
    nameof(IsExcess),
    nameof(Type),
    nameof(Id),
    nameof(BinId))]
public class Box : Location
{
    [JsonIgnore]
    public int BinId { get; init; }
    public Bin Bin { get; set; } = default!;

    // min is 0 to account for other loc types, should be min 10

    [Range(0, 10_000)]
    public int Capacity { get; set; }

    [StringLength(40)]
    public string? Appearance { get; set; }

    public bool IsExcess
    {
        get => Capacity == 0;
        private set { }
    }

    public static Box CreateExcess()
    {
        return new Box
        {
            Name = "Excess",
            Capacity = 0,
            Bin = new Bin
            {
                Name = "Excess"
            }
        };
    }
}


public class Bin
{
    [JsonIgnore]
    public int Id { get; init; }

    [StringLength(10)]
    public string Name { get; set; } = string.Empty;

    public List<Box> Boxes { get; init; } = new();
}
