
namespace MTGViewer.Data.Internal
{
    internal enum Discriminator
    {
        Invalid,

        Suggestion,
        Trade,

        Shared,
        Deck
    }
}