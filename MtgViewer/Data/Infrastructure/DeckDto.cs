using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MtgViewer.Data.Infrastructure;

public sealed class DeckDto : ConcurrentDto
{
    public int Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; }

    public IEnumerable<QuantityDto> Holds { get; init; } = Enumerable.Empty<QuantityDto>();
    public IEnumerable<QuantityDto> Wants { get; init; } = Enumerable.Empty<QuantityDto>();
    public IEnumerable<QuantityDto> Givebacks { get; init; } = Enumerable.Empty<QuantityDto>();

    [JsonConstructor]
    public DeckDto()
    { }

    public DeckDto(CardDbContext dbContext, DeckContext deckContext)
    {
        var deck = deckContext.Deck;

        Id = deck.Id;
        OwnerId = deck.OwnerId;

        Name = deck.Name;
        Color = deck.Color;

        Wants = deck.Wants.Select(w => new QuantityDto(dbContext, w));
        Holds = deck.Holds.Select(h => new QuantityDto(dbContext, h));
        Givebacks = deck.Givebacks.Select(g => new QuantityDto(dbContext, g));

        dbContext.CopyToken(this, deck);
    }

    internal DeckContext ToDeckContext(CardDbContext dbContext)
    {
        var deck = new Deck();

        dbContext.Entry(deck).CurrentValues.SetValues(this);

        deck.Holds.AddRange(
            Holds.Select(q => q.ToQuantity<Hold>(dbContext)));

        deck.Wants.AddRange(
            Wants.Select(q => q.ToQuantity<Want>(dbContext)));

        deck.Givebacks.AddRange(
            Givebacks.Select(q => q.ToQuantity<Giveback>(dbContext)));

        return new DeckContext(deck);
    }
}
