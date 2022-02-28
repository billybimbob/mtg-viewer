using System;
using System.Collections.Generic;
using System.Linq;
namespace MTGViewer.Data;

public class CardImage
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string ImageUrl { get; init; } = default!;
}


public class CardPreview
{
    public string Id { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? ManaCost { get; init; }
    public string SetName { get; init; } = default!;
    public Rarity Rarity { get; init; }
    public string ImageUrl { get; init; } = default!;
    public int Total { get; init; }
}


public record CardTotal(Card Card, int Total);


public class ChangePreview
{
    public bool ToBox { get; init; }
    public bool FromBox { get; init; }
    public string CardName { get; init; } = default!;
}


public class TransactionPreview
{
    public DateTime AppliedAt { get; init; }
    public IEnumerable<ChangePreview> Changes { get; init; } = Enumerable.Empty<ChangePreview>();
    public int Total { get; init; }
}


public enum BuildState
{
    Theorycraft,
    Built,
    Requesting
}


public class DeckPreview
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public Color Color { get; init; }
    public int CardTotal { get; init; }

    public bool HasWants { get; init; }
    public bool HasReturns { get; init; }
    public bool HasTradesTo { get; init; }
}