
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

        Want,
        GiveBack,
    }
}