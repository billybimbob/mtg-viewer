using System;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using MTGViewer.Data.Projections;

namespace MTGViewer.Data.Infrastructure;

public sealed class SuggestionDto
{
    public CardImage? Card { get; set; }

    public UserPreview? Receiver { get; set; }

    [Display(Name = "Suggest To A Specific Deck")]
    public DeckPreview? To { get; set; }

    [StringLength(80)]
    [Display(Name = "Add Comment")]
    public string? Comment { get; set; }

    public static string PropertyId<T>(Expression<Func<SuggestionDto, T>> property)
    {
        if (property.Body is not MemberExpression { Member.Name: string name })
        {
            return string.Empty;
        }

        return $"{nameof(SuggestionDto)}-{name}";
    }
}
