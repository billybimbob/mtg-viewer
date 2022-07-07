namespace MtgViewer.Data.Infrastructure;

internal sealed class MatchInput
{
    public MatchInput(Card card, bool hasDetails, int limit)
    {
        Card = card;
        HasDetails = hasDetails;
        Limit = limit;
    }

    public Card Card { get; }

    public bool HasDetails { get; }

    public int Limit { get; }

    private int _copies;
    public int Copies
    {
        get => _copies;
        set
        {
            if (value >= 0 && value <= Limit)
            {
                _copies = value;
            }
        }
    }
}
