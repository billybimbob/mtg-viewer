using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Services
{
    public interface ISharedStorage
    {
        IQueryable<Box> Boxes { get; }

        IQueryable<CardAmount> Cards { get; }

        Task<Transaction> ReturnAsync(IEnumerable<CardReturn> returns);

        Task<Transaction?> OptimizeAsync();
    }


    public record CardReturn(Card Card, int NumCopies, Deck? Deck = null);


    public static class SharedStorageExtensions
    {
        public static Task<Transaction> ReturnAsync(
            this ISharedStorage storage,
            CardReturn first, params CardReturn[] extra)
        {
            return storage.ReturnAsync(extra.Prepend(first));
        }

        public static Task<Transaction> ReturnAsync(
            this ISharedStorage storage, 
            Card card, int numCopies, Deck? deck = null)
        {
            return storage.ReturnAsync(new CardReturn(card, numCopies, deck));
        }
    }
}