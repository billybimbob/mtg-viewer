namespace MtgViewer.Data.Projections;

public sealed record HeldCard(Card Card, int Copies);

public sealed record CardImage
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
}

#region Card Previews

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

#endregion

public sealed record CardId
{
    public string Id { get; init; } = string.Empty;
    public string MultiverseId { get; init; } = string.Empty;
}

internal sealed record MulliganOption
{
    public CardPreview? Card { get; set; }
    public int Copies { get; set; }
}
