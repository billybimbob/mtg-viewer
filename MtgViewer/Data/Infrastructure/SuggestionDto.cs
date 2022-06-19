using System.ComponentModel.DataAnnotations;

using MtgViewer.Data.Projections;

namespace MtgViewer.Data.Infrastructure;

public sealed class SuggestionDto
{
    public CardImage? Card { get; set; }

    public PlayerPreview? Receiver { get; set; }

    [Display(Name = "Suggest To A Specific Deck")]
    public DeckPreview? To { get; set; }

    [StringLength(80)]
    [Display(Name = "Add Comment")]
    public string? Comment { get; set; }
}
