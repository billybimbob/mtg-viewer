using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(Type), nameof(Id))]
[Index(nameof(Name), nameof(Id))] // could be updated often
public abstract class Location : Concurrent
{
    [JsonIgnore]
    public int Id { get; init; }

    [JsonIgnore]
    internal LocationType Type { get; private set; }

    [StringLength(20, MinimumLength = 1)]
    public string Name { get; set; } = default!;

    public List<Hold> Holds { get; init; } = new();
}

internal enum LocationType
{
    Invalid,
    Deck,
    Unclaimed,
    Box,
    Excess,
    Deleted
}
