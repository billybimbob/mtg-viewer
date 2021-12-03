namespace MTGViewer.Data;

public class CardPreview
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;


    public static explicit operator CardPreview(Card card)
    {
        return new CardPreview
        {
            Id = card.Id, 
            Name = card.Name, 
            ImageUrl = card.ImageUrl
        };
    }
}