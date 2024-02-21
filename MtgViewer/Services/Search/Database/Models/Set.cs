using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Services.Search.Database;

[Table("sets")]
[Index("Code", IsUnique = true)]
public partial class Set
{
    [Key]
    [Column("code", TypeName = "VARCHAR(8)")]
    public string Code { get; set; } = string.Empty;

    [Column("block")]
    public string? Block { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [Column("parentCode")]
    public string? ParentCode { get; set; }

    [Column("releaseDate")]
    public string? ReleaseDate { get; set; }

    [Column("type")]
    public string? Type { get; set; }
}
