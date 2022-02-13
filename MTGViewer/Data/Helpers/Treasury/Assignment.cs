using System;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data.Internal;


internal readonly record struct BoxAssignment<TSource>(TSource Source, int NumCopies, Box Box);


internal enum AddScheme
{
    Exact,
    Approximate,
    Guess
}

internal enum TakeScheme
{
    Exact,
    Approximate
}

internal enum ReturnScheme
{
    Exact,
    Approximate,
    Guess
}

internal enum ExcessScheme
{
    Exact,
    Approximate,
}

internal enum OverflowScheme
{
    Exact,
    Approximate
}


internal static class AssignmentExtensions
{
    internal static IEnumerable<BoxAssignment<CardRequest>> AddAssignment(
        this TreasuryContext treasuryContext,
        IEnumerable<CardRequest> requests,
        AddScheme scheme)
    {
        if (treasuryContext is null)
        {
            throw new ArgumentNullException(nameof(treasuryContext));
        }

        return scheme switch
        {
            AddScheme.Approximate => AddHandler.Approximate(treasuryContext, requests),
            AddScheme.Guess => AddHandler.Guess(treasuryContext, requests),
            AddScheme.Exact or _ => AddHandler.Exact(treasuryContext, requests)
        };
    }


    internal static IEnumerable<BoxAssignment<Card>> TakeAssignment(
        this TreasuryContext treasuryContext,
        ExchangeContext exchangeContext,
        TakeScheme scheme)
    {
        if (treasuryContext is null)
        {
            throw new ArgumentNullException(nameof(treasuryContext));
        }

        return scheme switch
        {
            TakeScheme.Approximate => TakeHandler.Approximate(treasuryContext, exchangeContext),
            TakeScheme.Exact or _ => TakeHandler.Exact(treasuryContext, exchangeContext)
        };
    }


    internal static IEnumerable<BoxAssignment<Card>> ReturnAssignment(
        this TreasuryContext treasuryContext,
        ExchangeContext exchangeContext,
        ReturnScheme scheme)
    {
        if (treasuryContext is null)
        {
            throw new ArgumentNullException(nameof(treasuryContext));
        }

        return scheme switch
        {
            ReturnScheme.Approximate => ReturnHandler.Approximate(treasuryContext, exchangeContext),
            ReturnScheme.Guess => ReturnHandler.Guess(treasuryContext, exchangeContext),
            ReturnScheme.Exact or _ => ReturnHandler.Exact(treasuryContext, exchangeContext)
        };
    }


    internal static IEnumerable<BoxAssignment<Amount>> ExcessAssignment(
        this TreasuryContext treasuryContext,
        ExcessScheme scheme)
    {
        if (treasuryContext is null)
        {
            throw new ArgumentNullException(nameof(treasuryContext));
        }

        return scheme switch
        {
            ExcessScheme.Approximate => ExcessHandler.Approximate(treasuryContext),
            ExcessScheme.Exact or _ => ExcessHandler.Exact(treasuryContext)
        };
    }


    internal static IEnumerable<BoxAssignment<Amount>> OverflowAssignment(
        this TreasuryContext treasuryContext,
        OverflowScheme scheme)
    {
        if (treasuryContext is null)
        {
            throw new ArgumentNullException(nameof(treasuryContext));
        }

        return scheme switch
        {
            OverflowScheme.Approximate => OverflowHandler.Approximate(treasuryContext),
            OverflowScheme.Exact or _ => OverflowHandler.Exact(treasuryContext)
        };
    }


    private static class AddHandler
    {
        internal static IEnumerable<BoxAssignment<CardRequest>> Exact(
            TreasuryContext treasuryContext,
            IEnumerable<CardRequest> requests)
        {
            if (requests.All(cr => cr.NumCopies == 0))
            {
                yield break;
            }

            var (available, _, _, boxSpace) = treasuryContext;

            var availableCards = available.SelectMany(b => b.Cards);
            var cardRequests = requests.Select(cr => cr.Card);

            var existingSpots = ExactAddLookup(availableCards, cardRequests, boxSpace);

            foreach (CardRequest request in requests)
            {
                var (card, numCopies) = request;
                var possibleBoxes = existingSpots[card.Id];

                if (numCopies == 0 || !possibleBoxes.Any())
                {
                    continue;
                }

                var assignments = FitToBoxes(request, numCopies, possibleBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<CardRequest>> Approximate(
            TreasuryContext treasuryContext,
            IEnumerable<CardRequest> requests)
        {
            if (requests.All(cr => cr.NumCopies == 0))
            {
                yield break;
            }

            var (available, _, _, boxSpace) = treasuryContext;

            var availableCards = available.SelectMany(b => b.Cards);
            var cardRequests = requests.Select(cr => cr.Card);

            var existingSpots = ApproxAddLookup(availableCards, cardRequests, boxSpace);

            foreach (CardRequest request in requests)
            {
                var (card, numCopies) = request;
                var possibleBoxes = existingSpots[card.Name];

                if (numCopies == 0 || !possibleBoxes.Any())
                {
                    continue;
                }

                var assignments = FitToBoxes(request, numCopies, possibleBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<CardRequest>> Guess(
            TreasuryContext treasuryContext, 
            IEnumerable<CardRequest> requests)
        {
            if (requests.All(cr => cr.NumCopies == 0))
            {
                yield break;
            }

            var (available, _, excess, boxSpace) = treasuryContext;

            var boxSearch = new BoxSearcher(available);

            // descending so that the first added cards do not shift down the 
            // positioning of the sorted card amounts
            // each of the returned cards should have less effect on following returns
            // keep eye on

            var orderedRequests = requests
                .OrderByDescending(cr => cr.Card.Name)
                    .ThenByDescending(cr => cr.Card.SetName);

            foreach (var request in orderedRequests)
            {
                (Card card, int numCopies) = request;

                if (numCopies == 0)
                {
                    continue;
                }

                var bestBoxes = boxSearch
                    .FindBestBoxes(card)
                    .Union(available)
                    .Concat(excess);

                var assignments = FitToBoxes(request, numCopies, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }
    }


    private static class TakeHandler
    {
        internal static IEnumerable<BoxAssignment<Card>> Exact(
            TreasuryContext treasuryContext,
            ExchangeContext exchangeContext)
        {
            var wants = exchangeContext.Deck.Wants;

            if (wants.All(w => w.NumCopies == 0))
            {
                yield break;
            }

            var boxAmounts = treasuryContext.Amounts;
            var boxSpace = treasuryContext.BoxSpace;
            var wantCards = wants.Select(w => w.Card);

            // TODO: account for changing NumCopies while iter
            var exactReturns = ExactTakeLookup(boxAmounts, wantCards, boxSpace);

            foreach (var want in wants)
            {
                var idPositions = exactReturns[want.CardId];

                if (!idPositions.Any())
                {
                    continue;
                }

                var assignments = TakeFromBoxes(want.Card, want.NumCopies, idPositions);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<Card>> Approximate(
            TreasuryContext treasuryContext,
            ExchangeContext exchangeContext)
        {
            var wants = exchangeContext.Deck.Wants;

            if (wants.All(w => w.NumCopies == 0))
            {
                yield break;
            }

            var boxAmounts = treasuryContext.Amounts;
            var boxSpace = treasuryContext.BoxSpace;
            var wantCards = wants.Select(w => w.Card);

            // TODO: account for changing NumCopies while iter
            var approxReturns = ApproxTakeLookup(boxAmounts, wantCards, boxSpace);

            foreach (var want in wants)
            {
                var namePositions = approxReturns[want.Card.Name];

                if (!namePositions.Any())
                {
                    continue;
                }

                var assignments = TakeFromBoxes(want.Card, want.NumCopies, namePositions);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }
    }


    private static class ReturnHandler
    {
        internal static IEnumerable<BoxAssignment<Card>> Exact(
            TreasuryContext treasuryContext,
            ExchangeContext exchangeContext)
        {
            var (available, _, _, boxSpace) = treasuryContext;
            var giveBacks = exchangeContext.Deck.GiveBacks;

            if (!available.Any() || giveBacks.All(g => g.NumCopies == 0))
            {
                yield break;
            }

            var availableAmounts = available.SelectMany(b => b.Cards);
            var giveCards = giveBacks.Select(w => w.Card);

            // TODO: account for changing NumCopies while iter
            var exactMatch = ExactAddLookup(availableAmounts, giveCards, boxSpace);

            foreach (var giveBack in giveBacks)
            {
                var bestBoxes = exactMatch[giveBack.CardId];

                if (giveBack.NumCopies == 0 || !bestBoxes.Any())
                {
                    continue;
                }

                var assignments = FitToBoxes(giveBack.Card, giveBack.NumCopies, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<Card>> Approximate(
            TreasuryContext treasuryContext, 
            ExchangeContext exchangeContext)
        {
            var (available, _, _, boxSpace) = treasuryContext;
            var giveBacks = exchangeContext.Deck.GiveBacks;

            if (!available.Any() || giveBacks.All(g => g.NumCopies == 0))
            {
                yield break;
            }

            var availableAmounts = available.SelectMany(b => b.Cards);
            var giveCards = giveBacks.Select(w => w.Card);

            // TODO: account for changing NumCopies while iter
            var approxMatch = ApproxAddLookup(availableAmounts, giveCards, boxSpace);

            foreach (var giveBack in giveBacks)
            {
                var bestBoxes = approxMatch[giveBack.Card.Name];

                if (giveBack.NumCopies == 0 || !bestBoxes.Any())
                {
                    continue;
                }

                var assignments = FitToBoxes(giveBack.Card, giveBack.NumCopies, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<Card>> Guess(
            TreasuryContext treasuryContext,
            ExchangeContext exchangeContext)
        {
            var (available, _, excess, boxSpace) = treasuryContext;
            var giveBacks = exchangeContext.Deck.GiveBacks;

            if (giveBacks.All(g => g.NumCopies == 0))
            {
                yield break;
            }

            var boxSearch = new BoxSearcher(available);

            // descending so that the first added cards do not shift down the 
            // positioning of the sorted card amounts
            // each of the returned cards should have less effect on following returns
            // keep eye on

            var orderedGiveBacks = giveBacks
                .OrderByDescending(g => g.Card.Name)
                    .ThenByDescending(g => g.Card.SetName);

            foreach (var giveBack in orderedGiveBacks)
            {
                if (giveBack.NumCopies == 0)
                {
                    continue;
                }

                var bestBoxes = boxSearch
                    .FindBestBoxes(giveBack.Card)
                    .Union(available)
                    .Concat(excess);

                var assignments = FitToBoxes(giveBack.Card, giveBack.NumCopies, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }
    }


    private static class ExcessHandler
    {
        internal static IEnumerable<BoxAssignment<Amount>> Exact(TreasuryContext treasuryContext)
        {
            var (available, _, excessBoxes, boxSpace) = treasuryContext;

            var availableAmounts = available.SelectMany(b => b.Cards);
            var excessAmounts = excessBoxes.SelectMany(b => b.Cards);
            var excessCards = excessAmounts.Select(a => a.Card);

            if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
            {
                yield break;
            }

            // TODO: account for changing NumCopies while iter
            var exactMatch = ExactAddLookup(availableAmounts, excessCards, boxSpace);

            foreach (var excess in excessAmounts)
            {
                var bestBoxes = exactMatch[excess.CardId];

                if (!bestBoxes.Any())
                {
                    continue;
                }

                var assignments = FitToBoxes(excess, excess.NumCopies, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<Amount>> Approximate(TreasuryContext treasuryContext)
        {
            var (available, _, excessBoxes, boxSpace) = treasuryContext;

            var availableAmounts = available.SelectMany(b => b.Cards);
            var excessAmounts = excessBoxes.SelectMany(b => b.Cards);
            var excessCards = excessAmounts.Select(a => a.Card);

            if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
            {
                yield break;
            }

            var approxMatch = ApproxAddLookup(availableAmounts, excessCards, boxSpace);

            foreach (var excess in excessAmounts)
            {
                var bestBoxes = approxMatch[excess.Card.Name].Union(available);

                if (!bestBoxes.Any())
                {
                    continue;
                }

                var assignments = FitToBoxes(excess, excess.NumCopies, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }
    }


    private static class OverflowHandler
    {
        internal static IEnumerable<BoxAssignment<Amount>> Exact(TreasuryContext treasuryContext)
        {
            var (available, overflowBoxes, _, boxSpace) = treasuryContext;

            if (!available.Any() || !overflowBoxes.Any())
            {
                yield break;
            }

            var availableCards = available.SelectMany(b => b.Cards);
            var overflowAmounts = overflowBoxes.SelectMany(b => b.Cards);
            var overflowCards = overflowAmounts.Select(a => a.Card);

            var exactMatches = ExactAddLookup(availableCards, overflowCards, boxSpace);

            foreach (var source in overflowAmounts)
            {
                var bestBoxes = exactMatches[source.CardId];

                if (!bestBoxes.Any())
                {
                    continue;
                }

                if (source.Location is not Box sourceBox)
                {
                    continue;
                }

                int copiesAbove = boxSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
                if (copiesAbove <= 0)
                {
                    continue;
                }

                int minTransfer = Math.Min(source.NumCopies, copiesAbove);

                var assignments = FitToBoxes(source, minTransfer, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }


        internal static IEnumerable<BoxAssignment<Amount>> Approximate(TreasuryContext treasuryContext)
        {
            var (available, overflowBoxes, excess, boxSpace) = treasuryContext;

            if (!overflowBoxes.Any())
            {
                yield break;
            }

            var availableAmounts = available.SelectMany(b => b.Cards);
            var overflowAmounts = overflowBoxes.SelectMany(b => b.Cards);
            var overflowCards = overflowAmounts .Select(a => a.Card);

            var approxMatch = ApproxAddLookup(availableAmounts, overflowCards, boxSpace);

            foreach (var source in overflowAmounts)
            {
                if (source.Location is not Box sourceBox)
                {
                    continue;
                }

                int copiesAbove = boxSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
                if (copiesAbove <= 0)
                {
                    continue;
                }

                var bestBoxes = approxMatch[source.Card.Name]
                    .Union(available)
                    .Concat(excess);

                int minTransfer = Math.Min(source.NumCopies, copiesAbove);
                var assignments = FitToBoxes(source, minTransfer, bestBoxes, boxSpace);

                foreach (var assignment in assignments)
                {
                    yield return assignment;
                }
            }
        }
    }



    private static IEnumerable<BoxAssignment<TSource>> FitToBoxes<TSource>(
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


    private static IEnumerable<BoxAssignment<TSource>> TakeFromBoxes<TSource>(
        TSource source,
        int cardsToTake,
        IEnumerable<Amount> boxAmounts)
    {
        foreach (var amount in boxAmounts)
        {
            if (amount.Location is not Box box)
            {
                continue;
            }

            int takeCopies = Math.Min(cardsToTake, amount.NumCopies);
            if (takeCopies == 0)
            {
                continue;
            }

            yield return new BoxAssignment<TSource>(source, takeCopies, box);

            cardsToTake -= takeCopies;
            if (cardsToTake == 0)
            {
                yield break;
            }
        }
    }


    // add assignments should add to larger dup stacks
    // in boxes with more available space

    private static ILookup<string, Box> ExactAddLookup(
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


    private static ILookup<string, Box> ApproxAddLookup(
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


    // take assignments should take from smaller dup stacks first
    // in boxes with less available space

    private static ILookup<string, Amount> ExactTakeLookup(
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

            .OrderBy(a => a.NumCopies)
                .ThenBy(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            // lookup group orders should preserve NumCopies order
            .ToLookup(a => a.CardId);
    }


    private static ILookup<string, Amount> ApproxTakeLookup(
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
            .OrderBy(a => a.NumCopies)
                .ThenBy(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.Card.Name);
    }
}
