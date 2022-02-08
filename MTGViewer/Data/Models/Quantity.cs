using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Internal;
using MTGViewer.Data.Concurrency;

namespace MTGViewer.Data;

[Index(
    nameof(Type),
    nameof(CardId),
    nameof(LocationId), IsUnique = true)]
public abstract class Quantity : Concurrent
{
    [Key]
    [JsonIgnore]
    public int Id { get; private set; }

    [JsonIgnore]
    internal QuantityType Type { get; private set; }


    [JsonIgnore]
    public string CardId { get; init; } = default!;
    public Card Card { get; init; } = default!;


    [JsonIgnore]
    public int LocationId { get; init; }
    public Location Location { get; init; } = default!;

    // limit is kind of arbitrary

    [Display(Name = "Copies")]
    [Range(1, 4_096)]
    public int NumCopies { get; set; }
}


public class Amount : Quantity
{ }


public class Want : Quantity
{ }


public class GiveBack : Quantity
{ }