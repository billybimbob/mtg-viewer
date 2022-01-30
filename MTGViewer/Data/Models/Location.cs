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
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    internal Discriminator Type { get; private set; }

    [Required]
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
    public string OwnerId { get; init; } = null!;
    public UserRef Owner { get; init; } = null!;


    [Display(Name = "Give Backs")]
    public List<GiveBack> GiveBacks { get; init; } = new();


    [Display(Name = "Trades To")]
    public List<Trade> TradesTo { get; init; } = new();


    [Display(Name = "Trades From")]
    public List<Trade> TradesFrom { get; init; } = new();


    public string Colors { get; private set; } = string.Empty;

    public void UpdateColors(CardText toCardText)
    {
        var cardMana = Cards
            .Where(a => a.NumCopies > 0)
            .Select(a => a.Card.ManaCost)
            .SelectMany( toCardText.FindMana )
            .SelectMany(mana => mana.Value.Split('/'));

        var wantMana = Wants
            .Where(w => w.NumCopies > 0)
            .Select(w => w.Card.ManaCost)
            .SelectMany( toCardText.FindMana )
            .SelectMany(mana => mana.Value.Split('/'));

        var colorSymbols = Color.Symbols.Keys
            .Intersect( cardMana.Union(wantMana) )
            .Select( toCardText.ManaString );

        Colors = string.Join(string.Empty, colorSymbols);
    }
}


[Index(
    nameof(IsExcess),
    nameof(Capacity))]
public class Box : Location
{
    [JsonIgnore]
    public int BinId { get; init; }
    public Bin Bin { get; set; } = null!;

    [Range(10, 10_000)]
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
