using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using MTGViewer.Data;

namespace MTGViewer.Services;

/// <summary>
///  Manages modifications to the <see cref="Card"/> collection that is stored in any <see cref="Box"/>.
/// </summary>
public interface ITreasury
{
    /// <summary>
    /// Queries for <see cref="Box"/> information in the Treasury
    /// </summary>
    IQueryable<Box> Boxes { get; }


    /// <summary>
    /// Queries for Card <see cref="Amount"/> information in the Treasury 
    /// </summary>
    IQueryable<Amount> Cards { get; }


    /// <summary>
    /// Applies pending <see cref="Want"/> and <see cref="GiveBack"/> to the Treasury 
    /// </summary>
    /// <remarks>
    /// All changes applied only affect the Treasury no modifications are done to the 
    /// given <see cref="Deck"/>.
    /// </remarks>
    /// <returns> 
    /// A <see cref="Transaction"/> containing all of the card changes, or <see langword="null"/> 
    /// if no changes could be made. 
    /// </returns>
    /// <exception cref="OperationCanceledException"></exception>
    /// <exception cref="DbUpdateException"></exception>
    Task<Transaction?> ExchangeAsync(Deck deck, CancellationToken cancel = default);


    /// <summary> 
    /// Adding cards to the Treasury that prioritizes the least amount of <see cref="Change"/> values
    /// </summary>
    /// <remarks> 
    /// The priority of minimal changes may result in poor space utilization over time. 
    /// </remarks>
    /// <returns>
    /// A <see cref="Transaction"/> that contains all of the card changes, or <see langword="null" /> 
    /// if no changes could be made
    /// </returns>
    /// <exception cref="OperationCanceledException"></exception>
    /// <exception cref="DbUpdateException"></exception>
    /// <seealso cref="OptimizeAsync"/>
    Task<Transaction?> ReturnAsync(IEnumerable<CardReturn> returns, CancellationToken cancel = default);


    /// <summary>
    /// Increases space utilization of the Treasury storage, if possible
    /// </summary>
    /// <remarks>
    /// This operation may potentially generate massive bulk of <see cref="Change"/> values to 
    /// the Treasury.
    /// </remarks>
    /// <returns> 
    /// A <see cref="Transaction"/> containing all of the Treasury changes, or <see langword="null" /> 
    /// if no changes could be made
    /// </returns>
    /// <exception cref="OperationCanceledException"></exception>
    /// <exception cref="DbUpdateException"></exception>
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