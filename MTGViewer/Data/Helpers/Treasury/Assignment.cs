using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;


internal readonly record struct StorageAssignment<TSource>(TSource Source, int NumCopies, Storage Target);


internal static class Assignment
{
    public static IEnumerable<StorageAssignment<TSource>> FitToBoxes<TSource>(
        TSource source,
        int copiesToAssign,
        IEnumerable<Storage> storageOptions,
        IReadOnlyDictionary<Storage, int> storageSpace)
    {
        foreach (var storage in storageOptions)
        {
            if (storage is not Box box)
            {
                continue;
            }

            int spaceUsed = storageSpace.GetValueOrDefault(storage);
            int remainingSpace = Math.Max(0, box.Capacity - spaceUsed);

            int newCopies = Math.Min(copiesToAssign, remainingSpace);
            if (newCopies == 0)
            {
                continue;
            }

            yield return new StorageAssignment<TSource>(source, newCopies, storage);

            copiesToAssign -= newCopies;
            if (copiesToAssign == 0)
            {
                yield break;
            }
        }
        
        if (copiesToAssign > 0
            && storageOptions.OfType<Excess>().FirstOrDefault()
            is Excess firstExcess)
        {
            yield return new StorageAssignment<TSource>(source, copiesToAssign, firstExcess);
        }
    }


    // add assignments should add to larger dup stacks
    // in boxes with more available space

    public static ILookup<string, Storage> ExactAddLookup(
        IEnumerable<Amount> targets, 
        IEnumerable<Card> cards,
        IReadOnlyDictionary<Storage, int> storageSpace)
    {
        var cardIds = cards
            .Select(c => c.Id)
            .Distinct();

        // TODO: account for changing NumCopies while iter
        return targets
            .Join( cardIds,
                a => a.CardId, cid => cid,
                (target, _) => target)

            .OrderByDescending(a => a.Copies)
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - storageSpace.GetValueOrDefault(box),
                    Excess excess => -storageSpace.GetValueOrDefault(excess),
                    _ => throw new ArgumentException(nameof(targets))
                })            

            // lookup group orders should preserve NumCopies order
            .ToLookup(a => a.CardId, a => (Storage)a.Location);
    }


    public static ILookup<string, Storage> ApproxAddLookup(
        IEnumerable<Amount> targets, 
        IEnumerable<Card> cards,
        IReadOnlyDictionary<Storage, int> storageSpace)
    {
        var cardNames = cards
            .Select(c => c.Name)
            .Distinct();

        // TODO: account for changing NumCopies while iter
        return targets
            .Join( cardNames,
                a => a.Card.Name, cn => cn,
                (target, _) => target)

            // lookup group orders should preserve NumCopies order
            .OrderByDescending(a => a.Copies)
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - storageSpace.GetValueOrDefault(box),
                    Excess excess => -storageSpace.GetValueOrDefault(excess),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.Card.Name, a => (Storage)a.Location);
    }

}
