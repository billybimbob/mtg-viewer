
namespace MTGViewer.Data.Internal
{
    public enum SaveResult
    {
        None,
        Success,
        Error
    }


    internal enum Discriminator
    {
        Invalid,

        Box,
        Deck,

        BoxAmount,
        DeckAmount
    }
}


namespace MTGViewer.Data
{
    public enum Intent
    {
        None,
        Trade,
        Take,
        Return,
    }
}