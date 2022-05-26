using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(Type), nameof(LocationId), nameof(CardId), IsUnique = true)]
[Index(nameof(LocationId), nameof(Type))]
[Index(nameof(CardId), nameof(Type))]
public abstract class Quantity : Concurrent
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    internal QuantityType Type { get; private set; }

    [JsonIgnore]
    public Card Card { get; init; } = default!;
    public string CardId { get; init; } = default!;

    [JsonIgnore]
    public int LocationId { get; init; }
    public virtual Location Location { get; init; } = default!;

    // limit is kind of arbitrary

    [Display(Name = "Number of Copies")]
    [Range(1, 4_096)]
    public int Copies { get; set; }
}

public class Hold : Quantity
{ }

public class Want : Quantity
{
    public override Theorycraft Location => (Theorycraft)base.Location;
}

public class Giveback : Quantity
{
    public override Deck Location => (Deck)base.Location;
}
