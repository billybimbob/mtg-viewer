using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services.Internal;

namespace MTGViewer.Services;

public class TreasuryHandler
{
    private readonly ILogger<TreasuryHandler> _logger;

    public TreasuryHandler(ILogger<TreasuryHandler> logger)
    {
        _logger = logger;
    }


    private static IQueryable<Box> OrderedBoxes(CardDbContext dbContext)
    {
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        return dbContext.Boxes
            .Include(b => b.Cards
                .OrderBy(a => a.NumCopies))
                .ThenInclude(a => a.Card)
            .OrderBy(b => b.Id);
    }



    #region Add

    public Task AddAsync(
        CardDbContext dbContext, 
        Card card, 
        int numCopies,
        CancellationToken cancel = default)
    {
        var request = new []{ new CardRequest(card, numCopies) };

        return AddAsync(dbContext, request, cancel);
    }


    public async Task AddAsync(
        CardDbContext dbContext,
        IEnumerable<CardRequest> adding, 
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        adding = AsAddRequests(adding); 

        if (!adding.Any())
        {
            return;
        }

        var cards = adding.Select(cr => cr.Card);

        dbContext.Cards.AttachRange(cards);

        await OrderedBoxes(dbContext).LoadAsync(cancel); 

        if (!dbContext.Boxes.Local.Any())
        {
            throw new InvalidOperationException("There are no boxes to return to");
        }

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        ApplyExactAdds(treasuryContext, adding);
        ApplyApproxAdds(treasuryContext, adding);
        ApplyNewAdds(treasuryContext, adding);

        RemoveEmpty(dbContext);
    }


    private static IReadOnlyList<CardRequest> AsAddRequests(IEnumerable<CardRequest?>? requests)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        requests = requests.ToArray();

        if (requests.Any(req => req is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        // descending so that the first added cards do not shift down the 
        // positioning of the sorted card amounts
        // each of the returned cards should have less effect on following returns
        // keep eye on

        return requests
            .Cast<CardRequest>()
            .GroupBy(
                req => req.Card.Id,
                (_, requests) => requests.First() with
                {
                    NumCopies = requests.Sum(req => req.NumCopies)
                })
            .OrderByDescending(req => req.Card.Name)
                .ThenByDescending(req => req.Card.SetName)
            .ToList();
    }


    private static void ApplyExactAdds(
        TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        var (available, _, _, boxSpace) = treasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);
        var cardRequests = requests.Select(cr => cr.Card);

        var existingSpots = ExactLookup(availableCards, cardRequests, boxSpace);

        foreach (CardRequest request in requests)
        {
            var (card, numCopies) = request;
            var possibleBoxes = existingSpots[card.Id];

            if (!possibleBoxes.Any())
            {
                continue;
            }

            var splitToBoxes = FitToBoxes(possibleBoxes, boxSpace, numCopies);

            foreach ((Box box, int splitCopies) in splitToBoxes)
            {
                treasuryContext.AddCopies(card, splitCopies, box);

                request.NumCopies -= splitCopies;
            }
        }
    }


    private static void ApplyApproxAdds(
        TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        if (requests.All(cr => cr.NumCopies == 0))
        {
            return;
        }

        var (available, _, _, boxSpace) = treasuryContext;

        var availableCards = available.SelectMany(b => b.Cards);
        var cardRequests = requests.Select(cr => cr.Card);

        var existingSpots = ApproxLookup(availableCards, cardRequests, boxSpace);

        foreach (CardRequest request in requests)
        {
            var (card, numCopies) = request;
            var possibleBoxes = existingSpots[card.Name];

            if (!possibleBoxes.Any())
            {
                continue;
            }

            var splitToBoxes = FitToBoxes(possibleBoxes, boxSpace, numCopies);

            foreach ((Box box, int splitCopies) in splitToBoxes)
            {
                treasuryContext.AddCopies(card, splitCopies, box);

                request.NumCopies -= splitCopies;
            }
        }
    }


    private static void ApplyNewAdds(
        TreasuryContext treasuryContext, IEnumerable<CardRequest> requests)
    {
        if (requests.All(cr => cr.NumCopies == 0))
        {
            return;
        }

        var (available, _, excess, boxSpace) = treasuryContext;

        var boxSearch = new BoxSearcher(available);

        foreach ((Card card, int numCopies) in requests)
        {
            if (numCopies == 0)
            {
                continue;
            }

            var bestBoxes = boxSearch
                .FindBestBoxes(card)
                .Union(available);

            var newReturns = FitToBoxes(bestBoxes, boxSpace, numCopies, excess);

            foreach ((Box box, int splitCopies) in newReturns)
            {
                treasuryContext.AddCopies(card, splitCopies, box);
            }
        }
    }

    #endregion



    #region Exchange

    public async Task ExchangeAsync(
        CardDbContext dbContext, 
        Deck deck, 
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (deck is null)
        {
            throw new ArgumentNullException(nameof(deck));
        }

        var entry = dbContext.Entry(deck);

        if (entry.State is EntityState.Detached)
        {
            dbContext.Decks.Attach(deck);
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);
        var exchangeContext = new ExchangeContext(dbContext, treasuryContext);

        ApplyExactCheckouts(treasuryContext, exchangeContext);
        ApplyApproxCheckouts(treasuryContext, exchangeContext);

        ApplyExactReturns(treasuryContext, exchangeContext);
        ApplyApproxReturns(treasuryContext, exchangeContext);
        ApplyNewReturns(treasuryContext, exchangeContext);

        TransferExactExcess(treasuryContext);
        TransferApproxExcess(treasuryContext);

        RemoveEmpty(dbContext);
    }


    private static void ApplyExactCheckouts(
        TreasuryContext treasuryContext, ExchangeContext exchangeContext)
    {
        var wants = exchangeContext.Deck.Wants;

        if (wants.All(w => w.NumCopies == 0))
        {
            return;
        }

        var boxAmounts = treasuryContext.Amounts;
        var boxSpace = treasuryContext.BoxSpace;
        var wantCards = wants.Select(w => w.Card);

        // TODO: account for changing NumCopies while iter
        var exactReturns = ExactReturnLookup(boxAmounts, wantCards, boxSpace);

        foreach (var want in wants)
        {
            var idPositions = exactReturns[want.CardId];

            if (!idPositions.Any())
            {
                continue;
            }

            var boxTakes = TakeFromBoxes(idPositions, want.NumCopies);

            foreach ((Box box, int splitCopies) in boxTakes)
            {
                exchangeContext.TakeCopies(want.Card, splitCopies, box);
            }
        }
    }


    private static ILookup<string, Amount> ExactReturnLookup(
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

            // lookup group orders should preserve NumCopies order
            .OrderBy(a => a.NumCopies)
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.CardId);
    }


    private static void ApplyApproxCheckouts(
        TreasuryContext treasuryContext, ExchangeContext exchangeContext)
    {
        var wants = exchangeContext.Deck.Wants;

        if (wants.All(w => w.NumCopies == 0))
        {
            return;
        }

        var boxAmounts = treasuryContext.Amounts;
        var boxSpace = treasuryContext.BoxSpace;
        var wantCards = wants.Select(w => w.Card);

        // TODO: account for changing NumCopies while iter
        var approxReturns = ApproxReturnLookup(boxAmounts, wantCards, boxSpace);

        foreach (var want in wants)
        {
            var namePositions = approxReturns[want.Card.Name];

            if (!namePositions.Any())
            {
                continue;
            }

            var boxTakes = TakeFromBoxes(namePositions, want.NumCopies);

            foreach ((Box box, int splitCopies) in boxTakes)
            {
                exchangeContext.TakeCopies(want.Card, splitCopies, box);
            }
        }
    }


    private static ILookup<string, Amount> ApproxReturnLookup(
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
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.Card.Name);
    }


    private static void ApplyExactReturns(
        TreasuryContext treasuryContext, ExchangeContext exchangeContext)
    {
        var (available, _, _, boxSpace) = treasuryContext;
        var giveBacks = exchangeContext.Deck.GiveBacks;

        if (!available.Any() || giveBacks.All(g => g.NumCopies == 0))
        {
            return;
        }

        var availableAmounts = available.SelectMany(b => b.Cards);
        var giveCards = giveBacks.Select(w => w.Card);

        // TODO: account for changing NumCopies while iter
        var exactMatch = ExactLookup(availableAmounts, giveCards, boxSpace);

        foreach (var giveBack in giveBacks)
        {
            var bestBoxes = exactMatch[giveBack.CardId];

            if (giveBack.NumCopies == 0 || !bestBoxes.Any())
            {
                continue;
            }

            var boxReturns = FitToBoxes(bestBoxes, boxSpace, giveBack.NumCopies);

            foreach ((Box box, int splitCopies) in boxReturns)
            {
                exchangeContext.ReturnCopies(giveBack.Card, splitCopies, box);
            }
        }
    }


    private static void ApplyApproxReturns(
        TreasuryContext treasuryContext, ExchangeContext exchangeContext)
    {
        var (available, _, _, boxSpace) = treasuryContext;
        var giveBacks = exchangeContext.Deck.GiveBacks;

        if (!available.Any() || giveBacks.All(g => g.NumCopies == 0))
        {
            return;
        }

        var availableAmounts = available.SelectMany(b => b.Cards);
        var giveCards = giveBacks.Select(w => w.Card);

        // TODO: account for changing NumCopies while iter
        var approxMatch = ApproxLookup(availableAmounts, giveCards, boxSpace);

        foreach (var giveBack in giveBacks)
        {
            var bestBoxes = approxMatch[giveBack.Card.Name];

            if (giveBack.NumCopies == 0 || !bestBoxes.Any())
            {
                continue;
            }

            var boxReturns = FitToBoxes(bestBoxes, boxSpace, giveBack.NumCopies);

            foreach ((Box box, int splitCopies) in boxReturns)
            {
                exchangeContext.ReturnCopies(giveBack.Card, splitCopies, box);
            }
        }
    }


    private static void ApplyNewReturns(
        TreasuryContext treasuryContext, ExchangeContext exchangeContext)
    {
        var (available, _, excess, boxSpace) = treasuryContext;
        var giveBacks = exchangeContext.Deck.GiveBacks;

        if (!available.Any() || giveBacks.All(g => g.NumCopies == 0))
        {
            return;
        }

        var boxSearch = new BoxSearcher(available);

        foreach (var giveBack in giveBacks)
        {
            if (giveBack.NumCopies == 0)
            {
                continue;
            }

            var bestBoxes = boxSearch
                .FindBestBoxes(giveBack.Card)
                .Union(available);

            var newReturns = FitToBoxes(bestBoxes, boxSpace, giveBack.NumCopies, excess);

            foreach ((Box box, int splitCopies) in newReturns)
            {
                exchangeContext.ReturnCopies(giveBack.Card, splitCopies, box);
            }
        }
    }

    #endregion



    #region Update

    public async Task UpdateAsync(
        CardDbContext dbContext,
        Box updated, 
        CancellationToken cancel = default)
    {
        if (dbContext is null)
        {
            throw new ArgumentNullException(nameof(dbContext));
        }

        if (updated is null)
        {
            throw new ArgumentNullException(nameof(updated));
        }

        if (updated.IsExcess
            || await IsBoxUnchangedAsync(dbContext, updated, cancel))
        {
            return;
        }

        await OrderedBoxes(dbContext).LoadAsync(cancel);

        AddMissingExcess(dbContext);

        var treasuryContext = new TreasuryContext(dbContext);

        TransferExactExcess(treasuryContext);
        TransferApproxExcess(treasuryContext);
        TransferOverflow(treasuryContext);

        RemoveEmpty(dbContext);
    }


    private static async Task<bool> IsBoxUnchangedAsync(
        CardDbContext dbContext, Box updated, CancellationToken cancel)
    {
        var entry = dbContext.Entry(updated);

        if (entry.State is EntityState.Detached)
        {
            dbContext.Attach(updated);
        }

        if (entry.State is not EntityState.Unchanged)
        {
            return false;
        }

        var capacityMeta = entry.Property(b => b.Capacity).Metadata;

        return await entry.GetDatabaseValuesAsync(cancel) is var dbValues
            && dbValues?.GetValue<int>(capacityMeta) == updated.Capacity;
    }


    private static void TransferOverflow(TreasuryContext treasuryContext)
    {
        var (_, overflowBoxes, excess, boxSpace) = treasuryContext;

        if (!overflowBoxes.Any())
        {
            return;
        }

        var overflowCards = overflowBoxes.SelectMany(b => b.Cards);
        var nonAvailable = Array.Empty<Box>();

        foreach (var overflow in overflowCards)
        {
            if (overflow.Location is not Box sourceBox)
            {
                continue;
            }

            int copiesAbove = boxSpace.GetValueOrDefault(sourceBox) - sourceBox.Capacity;
            if (copiesAbove <= 0)
            {
                continue;
            }

            int minTransfer = Math.Min(overflow.NumCopies, copiesAbove);
            var boxTransfers = FitToBoxes(nonAvailable, boxSpace, minTransfer, excess);

            foreach ((Box box, int splitCopies) in boxTransfers)
            {
                treasuryContext.TransferCopies(overflow.Card, splitCopies, box, overflow.Location);
            }
        }
    }

    #endregion



    private static void TransferExactExcess(TreasuryContext treasuryContext)
    {
        var (available, _, excessBoxes, boxSpace) = treasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);
        var excessAmounts = excessBoxes.SelectMany(b => b.Cards);
        var excessCards = excessAmounts.Select(a => a.Card);

        if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
        {
            return;
        }

        // TODO: account for changing NumCopies while iter
        var exactRebalance = ExactLookup(availableAmounts, excessCards, boxSpace);

        foreach (var excess in excessAmounts)
        {
            var bestBoxes = exactRebalance[excess.CardId];

            if (!bestBoxes.Any())
            {
                continue;
            }

            var boxTransfers = FitToBoxes(bestBoxes, boxSpace, excess.NumCopies);

            foreach ((Box box, int splitCopies) in boxTransfers)
            {
                treasuryContext.TransferCopies(excess.Card, splitCopies, box, excess.Location);
            }
        }
    }


    private static void TransferApproxExcess(TreasuryContext treasuryContext)
    {
        var (available, _, excessBoxes, boxSpace) = treasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);
        var excessAmounts = excessBoxes.SelectMany(b => b.Cards);
        var excessCards = excessAmounts.Select(a => a.Card);

        if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
        {
            return;
        }

        var exactRebalance = ApproxLookup(availableAmounts, excessCards, boxSpace);

        foreach (var excess in excessAmounts)
        {
            var bestBoxes = exactRebalance[excess.Card.Name].Union(available);

            if (!bestBoxes.Any())
            {
                continue;
            }

            var boxTransfers = FitToBoxes(bestBoxes, boxSpace, excess.NumCopies);

            foreach ((Box box, int splitCopies) in boxTransfers)
            {
                treasuryContext.TransferCopies(excess.Card, splitCopies, box, excess.Location);
            }
        }
    }


    private static IEnumerable<(Box, int)> FitToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<Box, int> boxSpace,
        int cardsToAssign,
        IEnumerable<Box>? excess = null)
    {
        foreach (var box in boxes)
        {
            if (box.IsExcess)
            {
                continue;
            }

            int spaceUsed = boxSpace.GetValueOrDefault(box);
            int remainingSpace = Math.Max(0, box.Capacity - spaceUsed);

            int newCopies = Math.Min(cardsToAssign, remainingSpace);
            if (newCopies == 0)
            {
                continue;
            }

            yield return (box, newCopies);

            cardsToAssign -= newCopies;
            if (cardsToAssign == 0)
            {
                yield break;
            }
        }
        
        excess ??= Enumerable.Empty<Box>();

        if (cardsToAssign > 0
            && excess.FirstOrDefault() is Box firstExcess
            && firstExcess.IsExcess)
        {
            yield return (firstExcess, cardsToAssign);
        }
    }


    private static IEnumerable<(Box, int)> TakeFromBoxes(
        IEnumerable<Amount> boxAmounts,
        int cardsToTake)
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

            yield return (box, takeCopies);

            cardsToTake -= takeCopies;
            if (cardsToTake == 0)
            {
                yield break;
            }
        }
    }


    private static ILookup<string, Box> ExactLookup(
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

            // lookup group orders should preserve NumCopies order
            .OrderBy(a => a.NumCopies)
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.CardId, a => (Box)a.Location);
    }


    private static ILookup<string, Box> ApproxLookup(
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
                .ThenByDescending(a => a.Location switch
                {
                    Box box => box.Capacity - boxSpace.GetValueOrDefault(box),
                    _ => throw new ArgumentException(nameof(targets))
                })
            
            .ToLookup(a => a.Card.Name, a => (Box)a.Location);
    }


    private static void AddMissingExcess(CardDbContext dbContext)
    {
        if (!dbContext.Boxes.Local.Any(b => b.IsExcess))
        {
            var excessBox = new Box
            {
                Name = "Excess",
                Capacity = 0,
                Bin = new Bin
                {
                    Name = "Excess"
                }
            };
            
            dbContext.Boxes.Add(excessBox);
        }
    }


    private static void RemoveEmpty(CardDbContext dbContext)
    {
        var emptyAmounts = dbContext.Amounts.Local
            .Where(a => a.NumCopies == 0);

        var emptyWants = dbContext.Wants.Local
            .Where(w => w.NumCopies == 0);

        var emptyGiveBacks = dbContext.GiveBacks.Local
            .Where(g => g.NumCopies == 0);

        dbContext.Amounts.RemoveRange(emptyAmounts);
        dbContext.Wants.RemoveRange(emptyWants);
        dbContext.GiveBacks.RemoveRange(emptyGiveBacks);
    }
}