using System;

namespace MtgViewer.Data.Projections;

public sealed record SuggestionPreview
{
    public int Id { get; init; }
    public DateTime SentAt { get; init; }

    public string CardId { get; init; } = string.Empty;
    public string CardName { get; init; } = string.Empty;
    public string? CardManaCost { get; init; }

    public string? ToName { get; init; }
    public string? Comment { get; init; }
}
