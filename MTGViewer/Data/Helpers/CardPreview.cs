namespace MTGViewer.Data;

public class CardPreview
{
    public CardPreview()
    { }

    public string Id { get; set; }

    public string Name { get; set; }

    public string Image { get; set; }


    public static explicit operator CardPreview(Card card) => 
        new CardPreview
        {
            Id = card.Id,
            Name = card.Name,
            Image = card.ImageUrl
        };
}