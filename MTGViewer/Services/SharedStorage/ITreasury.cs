using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Services
{
    public interface ITreasury
    {
        IQueryable<Box> Boxes { get; }

        IQueryable<CardAmount> Cards { get; }

        Task<Transaction> ReturnAsync(IEnumerable<CardReturn> returns, CancellationToken cancel = default);

        Task<Transaction?> OptimizeAsync(CancellationToken cancel = default);
    }


    public record CardReturn(Card Card, int NumCopies, Deck? Deck = null);


    public static class TreasuryExtensions
    {
        public static Task<Transaction> ReturnAsync(
            this ITreasury treasury, 
            Card card, int numCopies, Deck? deck = null, 
            CancellationToken cancel = default)
        {
            var returns = new []{ new CardReturn(card, numCopies, deck) };

            return treasury.ReturnAsync(returns, cancel);
        }
    }
}