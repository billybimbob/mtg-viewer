using System;

namespace MtgViewer.Data.Projections;

public sealed record SuggestionPreview
{
    public required int Id { get; init; }

    public required DateTime SentAt { get; init; }

    public required CardLink Card { get; init; }

    public required string? ToName { get; init; }

    public required string? Comment { get; init; }
}
