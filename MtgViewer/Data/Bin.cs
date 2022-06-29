using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Data;

[Index(nameof(Name), nameof(Id))]
public class Bin
{
    [JsonIgnore]
    public int Id { get; init; }

    [StringLength(10, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public List<Box> Boxes { get; init; } = new();
}
