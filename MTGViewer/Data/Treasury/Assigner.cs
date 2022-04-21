using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Treasury;

internal static class Assigner
{
    public static IEnumerable<Assignment<TSource>> FitToStorage<TSource>(
        TSource source,
        int copiesToAssign,
        IEnumerable<Storage> storageOptions,
        IReadOnlyDictionary<LocationIndex, StorageSpace> storageSpaces)
    {
        foreach (var storage in storageOptions)
        {
            if (storage is not Box)
            {
                continue;
            }

            var index = (LocationIndex)storage;

            if (storageSpaces.GetValueOrDefault(index)
                is not { Remaining: > 0 and int remaining })
            {
                continue;
            }

            int newCopies = Math.Min(copiesToAssign, remaining);
            if (newCopies == 0)
            {
                continue;
            }

            yield return new Assignment<TSource>(source, newCopies, storage);

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
            yield return new Assignment<TSource>(source, copiesToAssign, firstExcess);
        }
    }

    // add assignments should add to larger dup stacks
    // in boxes with more available space

    public static ILookup<string, Storage> ExactAddLookup(IEnumerable<Hold> targets, IEnumerable<Card> cards)
    {
        var cardIds = cards
            .Select(c => c.Id)
            .Distinct();

        // TODO: account for changing Copies while iter
        // lookup group orders should preserve Copies order
        return targets
            .Join(cardIds,
                h => h.CardId, cid => cid,
                (target, _) => target)

            .OrderByDescending(h => h.Location is Box)
                .ThenByDescending(h => h.Copies)

            .ToLookup(h => h.CardId, h => (Storage)h.Location);
    }

    public static ILookup<string, Storage> ApproxAddLookup(IEnumerable<Hold> targets, IEnumerable<Card> cards)
    {
        var cardNames = cards
            .Select(c => c.Name)
            .Distinct();

        // TODO: account for changing Copies while iter
        // lookup group orders should preserve Copies order
        return targets
            .Join(cardNames,
                h => h.Card.Name, cn => cn,
                (target, _) => target)

            .OrderByDescending(h => h.Location is Box)
                .ThenByDescending(h => h.Copies)

            .ToLookup(h => h.Card.Name, h => (Storage)h.Location);
    }

}
