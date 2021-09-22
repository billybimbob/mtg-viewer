using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MTGViewer.Data;


namespace MTGViewer.Services
{
    public interface ISharedStorage
    {
        IQueryable<Box> Boxes { get; }

        IQueryable<CardAmount> Cards { get; }

        Task ReturnAsync(IEnumerable<(Card, int numCopies)> returns);

        Task OptimizeAsync();
    }


    public static class SharedStorageExtensions
    {
        public static Task ReturnAsync(
            this ISharedStorage storage, params (Card, int numCopies)[] returns)
        {
            return storage.ReturnAsync(returns.AsEnumerable());
        }

        public static Task ReturnAsync(this ISharedStorage storage, Card card, int numCopies)
        {
            return storage.ReturnAsync( (card, numCopies) );
        }
    }
}