using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGViewer.Data;

#nullable enable
namespace MTGViewer.Services;

/// <summary> Handles changes cards to Boxes (shared storage). </summary>
public interface ITreasury
{
    /// <summary> Queries for Box information in the Treasury </summary>
    /// <see cref="Box"/>
    IQueryable<Box> Boxes { get; }


    /// <summary> Queries for Card Amount information in the Treasury </summary>
    /// <see cref="Amount"/>
    IQueryable<Amount> Cards { get; }


    /// <summary>
    /// Determines if the Treasury currently can fulfill any of the wanted Cards
    /// </summary>
    /// <exception cref="OperationCanceledException"></exception>
    /// <see cref="Want"/>
    Task<bool> AnyWantsAsync(IEnumerable<Want> wants, CancellationToken cancel = default);


    /// <summary> Applies pending Deck Wants and GiveBacks to the Treasury </summary>
    /// <returns> 
    /// A Transaction containing all of the actual card amount changes or null if no 
    /// changes could be made. 
    /// </returns>
    /// <exception cref="OperationCanceledException"></exception>
    /// <remarks>
    /// All changes applied only affect the Treasury amounts and boxes, no modifications
    /// are done to the given Deck.
    /// </remarks>
    /// <see cref="Deck"/>
    /// <see cref="Want"/>
    /// <see cref="GiveBack"/>
    /// <see cref="Transaction"/>
    Task<Transaction?> ExchangeAsync(Deck deck, CancellationToken cancel = default);


    /// <summary> 
    /// Adding cards to the Treasury that prioritizes the least amount of Changes
    /// </summary>
    /// <returns>
    /// A Transaction that contains all of the card Changes, or null if no changes could be made
    /// </returns>
    /// <exception cref="OperationCanceledException"></exception>
    /// <remarks> 
    /// The priority of minimal Changes may result in poor space utilization over time. 
    /// </remarks>
    /// <see cref="Transaction"/>
    /// <see cref="Change"/>
    /// <seealso cref="OptimizeAsync"/>
    Task<Transaction?> ReturnAsync(IEnumerable<CardReturn> returns, CancellationToken cancel = default);


    /// <summary>
    /// Increases space utilization of the Treasury storage, if possible
    /// </summary>
    /// <returns> 
    /// A Transaction containing all of the Treasury changes, or null if no
    /// changes could be made
    /// </returns>
    /// <exception cref="OperationCanceledException"></exception>
    /// <remarks>
    /// This operation may potentially generate massive bulk Changes to the Treasury
    /// </remarks>
    Task<Transaction?> OptimizeAsync(CancellationToken cancel = default);


    // TODO: implement
    // Task<Transaction?> UndoAsync(int transactionId, CancellationToken cancel = default);
}


public record CardReturn(Card Card, int NumCopies, Deck? Deck = null);


public static class TreasuryExtensions
{
    public static Task<Transaction?> ReturnAsync(
        this ITreasury treasury, 
        Card card, int numCopies, Deck? deck = null, 
        CancellationToken cancel = default)
    {
        var returns = new []{ new CardReturn(card, numCopies, deck) };

        return treasury.ReturnAsync(returns, cancel);
    }
}