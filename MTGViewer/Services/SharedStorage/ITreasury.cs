using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGViewer.Data;

#nullable enable
namespace MTGViewer.Services;

/// <summary> Handles returning cards to Boxes. </summary>
public interface ITreasury
{
    IQueryable<Box> Boxes { get; }

    IQueryable<Amount> Cards { get; }

    /// <summary> 
    /// Adding cards to the Treasury that prioritizes the least amount of Changes
    /// </summary>
    /// <returns> A Transaction that contains all of the card Changes </returns>
    /// <remarks> 
    /// The priority of minimal Changes may result in poor space utilization over time. 
    /// </remarks>
    /// <see cref="Transaction"/>
    /// <see cref="Change"/>
    /// <seealso cref="OptimizeAsync"/>
    Task<Transaction> ReturnAsync(IEnumerable<CardReturn> returns, CancellationToken cancel = default);

    /// <summary>
    /// Increases space utilization of the Treasury storage, if possible
    /// </summary>
    /// <returns> 
    /// A Transaction containing all of the Treasury changes, or null if no
    /// changes could be made
    /// </returns>
    /// <remarks>
    /// This operation may potentially generate massive bulk Changes to the Treasury
    /// </remarks>
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