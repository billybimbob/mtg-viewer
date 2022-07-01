using System;

namespace MtgViewer.Data.Projections;

public sealed record SuggestionPreview
{
    public int Id { get; init; }

    public DateTime SentAt { get; init; }

    public CardLink Card { get; init; } = default!;

    public string? ToName { get; init; }

    public string? Comment { get; init; }
}
