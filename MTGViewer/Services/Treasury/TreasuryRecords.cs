using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MTGViewer.Data;

namespace MTGViewer.Services;


/// <summary>
/// Requests to either take or return  a <see cref="Data.Card"/> to the Treasury
/// </summary>
public record CardRequest(Card Card, int NumCopies)
{
    public Card Card { get; } = CardOrThrow(Card);

    public int NumCopies
    {
        get => _numCopies;
        set => _numCopies = NotNegativeOrThrow(value);
    }

    private int _numCopies = NotNegativeOrThrow(NumCopies);

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

    public IReadOnlyList<Amount> Changes { get; } = Changes;
    public IReadOnlyDictionary<int,int> OriginalCopies { get; } = OriginalCopies;
}



public static class DbTrackingExtensions
{
    public static void AttachResult(this CardDbContext dbContext, RequestResult result)
    {
        var (changes, originals) = result;

        dbContext.Amounts.AttachRange(changes);

        foreach (Amount change in changes)
        {
            if (originals.TryGetValue(change.Id, out int oldCopies))
            {
                var copyProperty = dbContext
                    .Entry(change)
                    .Property(a => a.NumCopies);

                copyProperty.OriginalValue = oldCopies;
            }
        }
    }
}