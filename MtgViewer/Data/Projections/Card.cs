namespace MtgViewer.Data.Projections;

public sealed record HeldCard(Card Card, int Copies);

public sealed record CardImage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
}

public record CardPreview
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    public string? ManaCost { get; init; }
    public float? ManaValue { get; init; }

    public string SetName { get; init; } = string.Empty;
    public Rarity Rarity { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
}

public record LocationCopy : CardPreview
{
    public int Held { get; init; }
}

public sealed record DeckCopy : LocationCopy
{
    public int Want { get; init; }
    public int Returning { get; init; }
}

public record CardLink
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SetName { get; init; } = string.Empty;
    public string? ManaCost { get; init; }
}

public sealed record DeleteLink : CardLink
{
    public bool HasDeckCopies { get; init; }
    public int StorageCopies { get; init; }
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

public sealed record CardId
{
    public string Id { get; init; } = string.Empty;
    public string MultiverseId { get; init; } = string.Empty;
}

internal sealed record CardCopy
{
    public CardPreview? Card { get; set; }
    public int Copies { get; set; }
}
