using System.Text.Json.Serialization;

namespace MtgViewer.Data.Infrastructure;

public sealed class QuantityDto : ConcurrentDto
{
    public int Id { get; init; }

    public string CardId { get; init; } = string.Empty;

    public int Copies { get; init; }

    [JsonConstructor]
    public QuantityDto()
    {
    }

    public QuantityDto(CardDbContext dbContext, Quantity quantity)
    {
        Id = quantity.Id;
        CardId = quantity.CardId;
        Copies = quantity.Copies;

        dbContext.CopyToken(this, quantity);
    }

    public TQuantity ToQuantity<TQuantity>(CardDbContext dbContext) where TQuantity : Quantity, new()
    {
        var quantity = new TQuantity();

        dbContext.Entry(quantity).CurrentValues.SetValues(this);

        return quantity;
    }
}
