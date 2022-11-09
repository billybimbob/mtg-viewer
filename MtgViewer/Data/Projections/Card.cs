namespace MtgViewer.Data.Projections;

public sealed record HeldCard(Card Card, int Copies);

public sealed record CardImage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ImageUrl { get; init; }
}

#region Card Previews

public record CardPreview
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public required string? ManaCost { get; init; }
    public float? ManaValue { get; init; }

    public required string SetName { get; init; }
    public required Rarity Rarity { get; init; }
    public required string ImageUrl { get; init; }
}

public record CardCopy : CardPreview
{
    public int Held { get; init; }
}

public sealed record DeckCopy : CardCopy
{
    public int Want { get; init; }
    public int Returning { get; init; }
}

#endregion

#region Card Links

public record CardLink
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string SetName { get; init; }
    public string? ManaCost { get; init; }
}

public sealed record DeleteLink : CardLink
{
    public required bool HasDeckCopies { get; init; }
    public required int StorageCopies { get; init; }
}

public record LocationLink : CardLink
{
    public int Held { get; init; }
}

public sealed record DeckLink : LocationLink
{
    public int Want { get; init; }
    public int Returning { get; init; }
}

#endregion

public sealed record CardId
{
    public required string Id { get; init; }
    public required string MultiverseId { get; init; }
}

internal sealed record MulliganOption
{
    public CardPreview? Card { get; set; }
    public int Copies { get; set; }
}
