using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGViewer.Data;

namespace MTGViewer.Services;


/// <summary>
/// Searches the Treasury to find positions where to add or remove cards
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
    /// Finds possible changes to <see cref="Amount"/> from all <see cref="Box"/> that can be withdrawn 
    /// for the given <see cref="Card"/> requests.
    /// </summary>
    /// <remarks> 
    /// No actual modifications are applied to the Treasury.
    /// The result will also include Treasury Cards with the same name if exact matches 
    /// are insufficient.
    /// </remarks>
    /// <returns>
    /// Modified <see cref="Amount"/> values specifying how the Treasury is allowed to be modified 
    /// to accommodate for the requested <see cref="Card"/> checkout.
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="OperationCanceledException"></exception>
    Task<RequestResult> FindCheckoutAsync(
        IEnumerable<CardRequest> requests,
        CancellationToken cancel = default);


    /// <summary>
    /// Finds possible changes to the Treasury to fit the returning <see cref="Card"/> copies.
    /// </summary>
    /// <remarks> 
    /// No actual modifications are applied to the Treasury,
    /// </remarks>
    /// <returns>
    /// Modified <see cref="Amount"/> values specifying how the Treasury is allowed to be modified 
    /// to accommodate for the requested <see cref="Card"/> returns.
    /// </returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="OperationCanceledException"></exception>
    Task<RequestResult> FindReturnAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default);
}


public static class TreasuryQueryExtensions
{
    public static Task<RequestResult> FindCheckoutAsync(
        this ITreasuryQuery treasury, 
        Card card, int numCopies, 
        CancellationToken cancel = default)
    {
        if (treasury is null)
        {
            throw new ArgumentNullException(nameof(treasury));
        }

        var request = new []{ new CardRequest(card, numCopies) };

        return treasury.FindCheckoutAsync(request, cancel);
    }


    public static Task<RequestResult> FindReturnAsync(
        this ITreasuryQuery treasury,
        Card card, int numCopies, CancellationToken cancel = default)
    {
        if (treasury is null)
        {
            throw new ArgumentNullException(nameof(treasury));
        }

        var request = new []{ new CardRequest(card, numCopies) };

        return treasury.FindReturnAsync(request, cancel);
    }
}