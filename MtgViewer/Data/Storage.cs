using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

public abstract class Storage : Location
{ }

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

public class Excess : Storage
{
    public static Excess Create()
        => new()
        {
            Name = nameof(Excess),
        };
}
