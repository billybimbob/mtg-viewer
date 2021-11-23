using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGViewer.Data;

namespace MTGViewer.Services;


/// <summary>
/// Searches the Treasury to finds positions where to add or remove cards
/// </summary>
public interface ITreasuryQuery
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
    /// Finds possible <see cref="Amount"/> from all <see cref="Box"/> that can be withdrawn 
    /// for the given <see cref="Card"/> requests.
    /// </summary>
    /// <remarks> 
    /// No actual modifications are applied to the Treasury, the returned <see cref="Withdrawl"/> values 
    /// are instructions on how the Treasury should be modified.
    /// The result will also include Treasury Cards with the same name if exact matches are insufficient
    /// </remarks>
    /// <returns>
    /// Instructions on how the Treasury is allowed to be modified to accommodate for 
    /// the requested <see cref="Card"/> checkout.
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="OperationCanceledException"></exception>
    Task<IReadOnlyList<Withdrawl>> FindCheckoutAsync(
        IEnumerable<CardRequest> requests, 
        IEnumerable<Alteration>? extraChanges = null,
        CancellationToken cancel = default);


    /// <summary>
    /// Finds all available <see cref="Box"/> that can fit the returning <see cref="Card"/> copies.
    /// </summary>
    /// <remarks> 
    /// No actual modifications are applied to the Treasury, the returned <see cref="Alteration"/> values 
    /// are instructions on how the Treasury should be modified.
    /// </remarks>
    /// <returns>
    /// Instructions on how the Treasury is allowed to be modified to accommodate for 
    /// the requested <see cref="Card"/> returns.
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="OperationCanceledException"></exception>
    Task<IReadOnlyList<Deposit>> FindReturnAsync(
        IEnumerable<CardRequest> requests, 
        IEnumerable<Alteration>? extraChanges = null,
        CancellationToken cancel = default);
}


public static class TreasuryQueryExtensions
{
    /// <seealso cref="ITreasuryQuery.FindCheckoutAsync(IEnumerable{CardRequest}, CancellationToken)"/>
    public static Task<IReadOnlyList<Withdrawl>> FindCheckoutAsync(
        this ITreasuryQuery treasury, 
        Card card, int numCopies, CancellationToken cancel = default)
    {
        if (treasury is null)
        {
            throw new ArgumentNullException(nameof(treasury));
        }

        var request = new []{ new CardRequest(card, numCopies) };

        return treasury.FindCheckoutAsync(request, null, cancel);
    }


    /// <seealso cref="ITreasuryQuery.FindReturnAsync(IEnumerable{CardRequest}, CancellationToken)"/>
    public static Task<IReadOnlyList<Deposit>> FindReturnAsync(
        this ITreasuryQuery treasury,
        Card card, int numCopies, CancellationToken cancel = default)
    {
        if (treasury is null)
        {
            throw new ArgumentNullException(nameof(treasury));
        }

        var request = new []{ new CardRequest(card, numCopies) };

        return treasury.FindReturnAsync(request, null, cancel);
    }
}


public record CardRequest(Card Card, int NumCopies)
{
    private Card _card = CardOrThrow(Card);
    private int _numCopies = PositiveOrThrow(NumCopies);

    public Card Card 
    {
        get => _card; 
        init => _card = CardOrThrow(value);
    }

    public int NumCopies
    {
        get => _numCopies;
        init => _numCopies = PositiveOrThrow(value);
    }
        
    private static Card CardOrThrow(Card card) =>
        card ?? throw new ArgumentNullException(nameof(Card));

    private static int PositiveOrThrow(int copies) =>
        copies > 0 ? copies : throw new ArgumentException(nameof(NumCopies));
}
