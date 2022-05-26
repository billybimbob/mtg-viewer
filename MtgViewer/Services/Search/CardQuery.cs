using System;
using System.ComponentModel.DataAnnotations;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public sealed record CardQuery
{
    public string? Id { get; set; }

    // [Display(Name = "Multiverse Id")]
    // public string? MultiverseId { get; set; }

    [StringLength(50)]
    public string? Name { get; set; }

    [Display(Name = "Mana Value")]
    [Range(0, 1_000_000)]
    public int? Cmc { get; set; }

    public Color Colors { get; set; }

    public Rarity? Rarity { get; set; }

    [Display(Name = "Set Name")]
    [StringLength(30)]
    public string? SetName { get; set; }

    [Display(Name = "Type(s)")]
    [StringLength(60)]
    public string? Type { get; set; }

    [StringLength(30)]
    public string? Artist { get; set; }

    [StringLength(5)]
    public string? Power { get; set; }

    [StringLength(5)]
    public string? Toughness { get; set; }

    [StringLength(5)]
    [Display(Name = "Starting Loyalty")]
    public string? Loyalty { get; set; }

    [StringLength(40)]
    [Display(Name = "Oracle Text")]
    public string? Text { get; set; }

    [StringLength(40)]
    [Display(Name = "Flavor Text")]
    public string? Flavor { get; set; }

    [Range(0, int.MaxValue)]
    public int Page { get; set; }

    [Range(0, int.MaxValue)]
    public int PageSize { get; set; }
}
