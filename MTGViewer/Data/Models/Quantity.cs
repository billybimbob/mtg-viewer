using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Internal;
using MTGViewer.Data.Concurrency;

namespace MTGViewer.Data;

[Index(
    nameof(Type),
    nameof(LocationId),
    nameof(CardId), IsUnique = true)]
public abstract class Quantity : Concurrent
{
    [JsonInclude]
    public int Id { get; set; }

    [JsonIgnore]
    internal Discriminator Type { get; private set; }


    [JsonIgnore]
    public Card Card { get; init; } = null!;
    public string CardId { get; init; } = null!;


    [JsonIgnore]
    public Location Location { get; init; } = null!;
    public int LocationId { get; init; }


    [Display(Name = "Copies")]
    [Range(1, int.MaxValue)]
    public int NumCopies { get; set; }
}


public class Amount : Quantity
{ }


public class Want : Quantity
{ }


public class GiveBack : Quantity
{ }