using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;


internal readonly record struct BoxAssignment<TSource>(TSource Source, int NumCopies, Box Box);


internal static class Assignment
{
    public static IEnumerable<BoxAssignment<TSource>> FitToBoxes<TSource>(
        TSource source,
        int copiesToAssign,
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<Box, int> boxSpace)
    {
        foreach (var box in boxes)
        {
            if (box.IsExcess)
            {
                continue;
            }

            int spaceUsed = boxSpace.GetValueOrDefault(box);
            int remainingSpace = Math.Max(0, box.Capacity - spaceUsed);

            int newCopies = Math.Min(copiesToAssign, remainingSpace);
            if (newCopies == 0)
            {
                continue;
            }

            yield return new BoxAssignment<TSource>(source, newCopies, box);

            copiesToAssign -= newCopies;
            if (copiesToAssign == 0)
            {
                yield break;
            }
        }
        
        if (copiesToAssign > 0
            && boxes.FirstOrDefault(b => b.IsExcess) is Box firstExcess)
        {
            yield return new BoxAssignment<TSource>(source, copiesToAssign, firstExcess);
        }
    }


    // add assignments should add to larger dup stacks
    // in boxes with more available space

    public static ILookup<string, Box> ExactAddLookup(
        IEnumerable<Amount> targets, 
        IEnumerable<Card> cards,
        IReadOnlyDictionary<Box, int> boxSpace)
    {
        var cardIds = cards
            .Select(c => c.Id)
            .Distinct();

        // TODO: account for changing NumCopies while iter
        return targets
            .Join( cardIds,
                a => a.CardId, cid => cid,
                (target, _) => target)

            .OrderByDescending(a => a.NumCopies)
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })            

            // lookup group orders should preserve NumCopies order
            .ToLookup(a => a.CardId, a => (Box)a.Location);
    }


    public static ILookup<string, Box> ApproxAddLookup(
        IEnumerable<Amount> targets, 
        IEnumerable<Card> cards,
        IReadOnlyDictionary<Box, int> boxSpace)
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
            .OrderByDescending(a => a.NumCopies)
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.Card.Name, a => (Box)a.Location);
    }

}
