using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using MTGViewer.Data;

namespace MTGViewer.Services;


/// <summary>
/// Requests to either take or return  a <see cref="Data.Card"/> to the Treasury
/// </summary>
public record CardRequest(Card Card, int NumCopies)
{
    private Card _card = CardOrThrow(Card);
    private int _numCopies = NotNegativeOrThrow(NumCopies);

    public Card Card
    {
        get => _card;
        init => _card = CardOrThrow(value);
    }

    public int NumCopies
    {
        get => _numCopies;
        set => _numCopies = NotNegativeOrThrow(value);
    }

    private static Card CardOrThrow(Card card) =>
        card ?? throw new ArgumentNullException(nameof(Card));

    private static int NotNegativeOrThrow(int copies) =>
        copies >= 0 ? copies : throw new ArgumentException(nameof(NumCopies));
}


/// <summary>
/// The modified Treasury amounts including the original copy values
/// </summary>
public record RequestResult(IReadOnlyList<Amount> Changes, IReadOnlyDictionary<int, int> OriginalCopies)
{
    private static readonly Lazy<RequestResult> _empty = new(() => 
        new RequestResult(
            Array.Empty<Amount>(), ImmutableDictionary<int,int>.Empty));

    public static RequestResult Empty => _empty.Value;

    public IReadOnlyList<Amount> Changes { get; init; } = Changes;
    public IReadOnlyDictionary<int,int> OriginalCopies { get; init; } = OriginalCopies;
}



public static class DbTrackingExtensions
{
    public static void AttachResult(this CardDbContext dbContext, RequestResult result)
    {
        var (modifies, originals) = result;

        dbContext.Amounts.AttachRange(modifies);

        foreach (Amount modify in modifies)
        {
            if (originals.TryGetValue(modify.Id, out int oldCopies))
            {
                var numCopiesProp = dbContext
                    .Entry(modify)
                    .Property(a => a.NumCopies);
                    
                numCopiesProp.OriginalValue = oldCopies;
            }
        }

        var emptyAmounts = modifies.Where(a => a.NumCopies <= 0);

        dbContext.Amounts.RemoveRange(emptyAmounts);
    }
}