using System;
using System.ComponentModel.DataAnnotations;
using MtgApiManager.Lib.Service;

namespace MTGViewer.Data;

public class CardSearch : IQueryParameter
{
    public string Id { get; }

    [StringLength(50)]
    public string Name { get; set; }

    [Display(Name = "Converted Mana Cost")]
    [Range(0, 1_000_000)]
    public int? Cmc { get; set; }

    public string Colors { get; set; }

    [StringLength(40)]
    public string Rarity { get; set; }

    [Display(Name = "Set Name")]
    [StringLength(30)]
    public string SetName { get; set; }

    [Display(Name = "Supertype(s)")]
    [StringLength(40)]
    public string Supertypes { get; set; }

    [Display(Name = "Type(s)")]
    [StringLength(40)]
    public string Types { get; set; }

    [Display(Name = "Subtype(s)")]
    [StringLength(40)]
    public string Subtypes { get; set; }

    [StringLength(30)]
    public string Artist { get; set; }

    [StringLength(5)]
    public string Power { get; set; }

    [StringLength(5)]
    public string Toughness { get; set; }

    [StringLength(5)]
    public string Loyalty { get; set; }
}