using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MTGViewer.Data;

#nullable enable
namespace MTGViewer.Services;


public sealed class FlatVariableStorage : ITreasury, IDisposable
{
    private readonly CardDbContext _dbContext;
    private readonly SemaphoreSlim _lock; // needed since CardDbContext is not thread safe
    private readonly ILogger<FlatVariableStorage> _logger;

    public FlatVariableStorage(CardDbContext dbContext, ILogger<FlatVariableStorage> logger)
    {
        _dbContext = dbContext;
        _lock = new(1, 1);
        _logger = logger;
    }


    public void Dispose()
    {
        _lock.Dispose();
    }


    public IQueryable<Box> Boxes => 
        _dbContext.Boxes
            .AsNoTrackingWithIdentityResolution();

    public IQueryable<Amount> Cards => 
        _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .AsNoTrackingWithIdentityResolution();


    private IOrderedQueryable<Box> SortedBoxes =>
        _dbContext.Boxes.OrderBy(s => s.Id);

    private IOrderedQueryable<Amount> SortedAmounts =>
        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position

        _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .Include(ca => ca.Card)
            .OrderBy(ca => ca.Card.Name)
                .ThenBy(ca => ca.Card.SetName); 



    public Task<bool> AnyWantsAsync(
        IEnumerable<Want> wants, CancellationToken cancel = default)
    {
        if (wants is null || !wants.Any())
        {
            return Task.FromResult(false);
        }

        var wantNames = wants
            .Where(w => w.NumCopies > 0)
            .Select(w => w.Card.Name)
            .Distinct()
            .ToArray();

        return Cards
            .Where(a => a.NumCopies > 0 && wantNames.Contains(a.Card.Name))
            .AnyAsync(cancel);
    }



    #region Exchange

    public async Task<Transaction?> ExchangeAsync(Deck deck, CancellationToken cancel = default)
    {
        if (InvalidDeck(deck))
        {
            return null;
        }

        await _lock.WaitAsync(cancel);

        try
        {
            var sortedBoxes = await SortedBoxes.ToArrayAsync(cancel); // unbounded: keep eye on

            if (!sortedBoxes.Any())
            {
                return null;
            }

            var sortedAmounts = await SortedAmounts
                .ThenByDescending(a => (a.Location as Box)!.Capacity)
                .ToArrayAsync(cancel); // unbounded: keep eye on

            if (!sortedAmounts.Any())
            {
                return null;
            }

            Transaction? newTransaction = null;

            var boxReturns = GetDeckReturns(deck);

            newTransaction = ApplyBoxReturns(sortedBoxes, sortedAmounts, boxReturns);
            newTransaction = ApplyWants(newTransaction, deck, sortedAmounts);

            if (newTransaction is not null)
            {
                // intentionally leave db exception unhandled
                await _dbContext.SaveChangesAsync(cancel);
            }

            return newTransaction;
        }
        finally
        {
            _lock.Release();
        }
    }


    private bool InvalidDeck(Deck deck)
    {
        bool InvalidQuantity(Quantity quantity) => 
            quantity.Id == default || quantity.NumCopies < 0;

        bool InvalidGiveBack((GiveBack, IEnumerable<Amount>) pair)
        {
            var (giveBack, amounts) = pair;

            if (amounts.Count() > 1)
            {
                return true;
            }

            var amount = amounts.FirstOrDefault();
            return amount == default 
                || amount.NumCopies < giveBack.NumCopies;
        }

        return deck is null 
            || deck.Id == default

            || !deck.Wants.Any() && !deck.Cards.Any() && !deck.GiveBacks.Any()

            || deck.Wants.Any( InvalidQuantity )
            || deck.Cards.Any( InvalidQuantity )
            || deck.GiveBacks.Any( InvalidQuantity )

            || deck.GiveBacks
                .GroupJoin( deck.Cards,
                    g => g.CardId,
                    a => a.CardId,
                    (giveBack, amounts) => (giveBack, amounts))
                .Any( InvalidGiveBack );
    }


    private Transaction? ApplyWants(Transaction? transaction, Deck deck, IReadOnlyList<Amount> boxAmounts)
    {
        var activeWants = deck.Wants
            .Where(w => w.NumCopies > 0)
            .ToHashSet();

        var validAmounts = boxAmounts.Where(ca => ca.NumCopies > 0);

        if (!activeWants.Any() || !validAmounts.Any())
        {
            return transaction;
        }

        transaction ??= new();
        var changes = transaction.Changes;

        ApplyExactWants(changes, activeWants, validAmounts, deck);
        ApplyApproxWants(changes, activeWants, validAmounts, deck);

        var emptyAvails = boxAmounts.Except(validAmounts);

        _dbContext.Amounts.RemoveRange(emptyAvails);
        _dbContext.Transactions.Add(transaction);

        return transaction;
    }


    private void ApplyExactWants(
        ICollection<Change> changes,
        ICollection<Want> wants,
        IEnumerable<Amount> availables,
        Deck deck)
    {
        var exactMatches = wants
            .Join( availables,
                want => want.CardId,
                avail => avail.CardId,
                (want, avails) => (want, avails))
            .ToList();

        foreach (var (want, available) in exactMatches)
        {
            if (!wants.Contains(want))
            {
                continue;
            }

            int amountTaken = Math.Min(want.NumCopies, available.NumCopies);

            if (amountTaken == want.NumCopies)
            {
                wants.Remove(want);
            }

            if (amountTaken == 0)
            {
                continue;
            }

            available.NumCopies -= amountTaken;

            var newChange = new Change
            {
                Card = available.Card,
                From = available.Location,
                To = deck,
                Amount = amountTaken
            };

            changes.Add(newChange);
        }
    }


    private void ApplyApproxWants(
        ICollection<Change> changes,
        IEnumerable<Want> wants,
        IEnumerable<Amount> availables,
        Deck deck)
    {
        var wantsByName = wants
            .GroupBy(w => w.Card.Name,
                (_, ws) => new WantNameGroup(ws));

        var availsByName = availables
            .GroupBy(ca => ca.Card.Name,
                (_, cas) => new CardNameGroup(cas));

        var closeMatches = wantsByName
            .Join( availsByName,
                wantGroup => wantGroup.Name,
                availGroup => availGroup.Name,
                (wantGroup, availGroup) => (wantGroup, availGroup));

        foreach (var (want, availGroup) in closeMatches)
        {
            using var closeAvails = availGroup.GetEnumerator();
            int wantAmount = want.NumCopies;

            while (wantAmount > 0 && closeAvails.MoveNext())
            {
                var currentAvail = closeAvails.Current;
                int amountTaken = Math.Min(wantAmount, currentAvail.NumCopies);

                currentAvail.NumCopies -= amountTaken;
                wantAmount -= amountTaken;

                var newChange = new Change
                {
                    Card = currentAvail.Card,
                    From = currentAvail.Location,
                    To = deck,
                    Amount = amountTaken
                };

                changes.Add(newChange);
            }
        }
    }


    private IReadOnlyList<CardReturn> GetDeckReturns(Deck deck)
    {
        return deck.Cards
            .Join( deck.GiveBacks,
                ca => ca.CardId,
                gb => gb.CardId,
                (actual, giveBack) => (actual, giveBack))

                // TODO: change to atomic returns, all or nothing
            .Where(ag => ag.actual.NumCopies >= ag.giveBack.NumCopies)
            .Select(ag => 
                new CardReturn(ag.giveBack.Card, ag.giveBack.NumCopies, deck))
            .ToList();
    }

    #endregion



    #region Return

    public async Task<Transaction?> ReturnAsync(
        IEnumerable<CardReturn> returns, CancellationToken cancel = default)
    {
        if (InvalidReturns(returns))
        {
            return null;
        }

        await _lock.WaitAsync(cancel);

        try
        {
            var sortedBoxes = await SortedBoxes.ToArrayAsync(cancel); // unbounded: keep eye on

            if (!sortedBoxes.Any())
            {
                return null;
            }

            var sortedAmounts = await SortedAmounts
                .ThenByDescending(a => (a.Location as Box)!.Capacity)
                .ToArrayAsync(cancel); // unbounded: keep eye on

            if (!sortedAmounts.Any())
            {
                return null;
            }

            var newTransaction = ApplyBoxReturns(sortedBoxes, sortedAmounts, returns);

            if (newTransaction is not null)
            {
                // intentionally leave db exception unhandled
                await _dbContext.SaveChangesAsync(cancel);
            }

            return newTransaction;
        }
        finally
        {
            _lock.Release();
        }
    }


    private bool InvalidReturns(IEnumerable<CardReturn> returns)
    {
        return !(returns?.Any() ?? false)
            || returns.Any(cr => cr.Card == null || cr.NumCopies <= 0);
    }


    private Transaction? ApplyBoxReturns(
        IReadOnlyList<Box> sortedBoxes, 
        IReadOnlyList<Amount> sortedAmounts, 
        IEnumerable<CardReturn> returns)
    {
        var newTransaction = new Transaction();
        var changes = newTransaction.Changes;

        var mergedReturns = MergedReturns(returns).ToList();

        var boxSpace = sortedBoxes
            .ToDictionary(b => b.Id, b => b.Cards.Sum(a => a.NumCopies));

        var unfinished = new List<CardReturn>();
        var newAmounts = new List<Amount>();

        var splitAmounts = ExistingBoxAdds(sortedAmounts, mergedReturns, boxSpace);

        ApplyExistingReturns(splitAmounts, unfinished, changes, boxSpace);

        if (unfinished.Any())
        {
            var returnPairs = NewBoxAdds(unfinished, sortedAmounts, sortedBoxes, boxSpace);

            ApplyNewReturns(returnPairs, newAmounts, changes, boxSpace);
        }

        if (!changes.Any())
        {
            return null;
        }
        
        var emptyAmounts = sortedAmounts
            .Concat(newAmounts)
            .Where(a => a.NumCopies == 0);

        _dbContext.Amounts.AttachRange(newAmounts);
        _dbContext.Amounts.RemoveRange(emptyAmounts);

        _dbContext.Transactions.Attach(newTransaction);

        return newTransaction;
    }


    private IEnumerable<CardReturn> MergedReturns(IEnumerable<CardReturn> returns)
    {
        return returns
            .GroupBy( cr => (cr.Card, cr.Deck),
                (cd, crs) =>
                    new CardReturn(cd.Card, crs.Sum(cr => cr.NumCopies), cd.Deck))

            // descending so that the first added cards do not shift down the 
            // positioning of the sorted card amounts
            // each of the returned cards should have less effect on following returns
            // TODO: keep eye on

            .OrderByDescending(cr => cr.Card.Name)
                .ThenByDescending(cr => cr.Card.SetName);
    }


    private IEnumerable<(CardReturn, IReadOnlyList<(Amount, int)>)> ExistingBoxAdds(
        IReadOnlyList<Amount> amounts,
        IReadOnlyList<CardReturn> returning,
        IReadOnlyDictionary<int, int> boxSpace)
    {
        var returnBoxes = amounts
            .ToLookup(ca => ca.CardId, ca => (Box)ca.Location);

        foreach (var cardReturn in returning)
        {
            var (card, numCopies, _) = cardReturn;

            if (!returnBoxes.Contains(cardReturn.Card.Id))
            {
                var empty = Array.Empty<(Amount, int)>();

                yield return (cardReturn, empty);
                continue;
            }

            var boxOptions = returnBoxes[card.Id];

            var splitBoxAmounts = FitToBoxes(boxOptions, boxSpace, numCopies);

            var splitReturns = amounts
                .Join( splitBoxAmounts,
                    amt => (amt.CardId, amt.LocationId),
                    boxAmt => (card.Id, boxAmt.Box.Id),
                    (amt, boxAmt) => (amt, boxAmt.Amount))
                .ToList();

            yield return (cardReturn, splitReturns);
        }
    }


    private void ApplyExistingReturns(
        IEnumerable<(CardReturn, IReadOnlyList<(Amount, int)>)> splitReturns,
        IList<CardReturn> unfinished,
        IList<Change> changes,
        IDictionary<int, int> boxSpace)
    {
        foreach (var (cardReturn, splits) in splitReturns)
        {
            var (card, numCopies, source) = cardReturn;

            if (splits.Count == 0)
            {
                unfinished.Add(cardReturn);
                continue;
            }
            
            int totalReturn = 0;

            foreach (var (target, splitAmount) in splits)
            {
                var newChange = new Change
                {
                    Card = target.Card,
                    To = target.Location,
                    From = source,
                    Amount = splitAmount
                };

                changes.Add(newChange);

                target.NumCopies += splitAmount;
                boxSpace[target.LocationId] += splitAmount;

                totalReturn += splitAmount;
            }

            int notReturned = numCopies - totalReturn;

            if (notReturned != 0)
            {
                unfinished.Add(cardReturn with { NumCopies = notReturned });
            }
        }
    }



    private IEnumerable<(Card, int, Deck?, Box)> NewBoxAdds(
        IReadOnlyList<CardReturn> newReturns,
        IReadOnlyList<Amount> sortedAmounts,
        IReadOnlyList<Box> sortedBoxes,
        IReadOnlyDictionary<int, int> boxSpace)
    {
        var cardComparer = new CardNameComparer();

        var sortedCards = sortedAmounts
            .Select(ca => ca.Card)
            .ToList();

        var positions = GetAddPositions(sortedAmounts).ToList();
        var boundaries = GetBoxBoundaries(sortedBoxes).ToList();
        
        foreach (var (returnCard, numCopies, source) in newReturns)
        {
            var amountIndex = sortedCards.BinarySearch(returnCard, cardComparer);
            bool cardExists = amountIndex >= 0;

            var card = cardExists ? sortedCards[amountIndex] : returnCard;

            if (!cardExists)
            {
                amountIndex = ~amountIndex;
            }

            var addPosition = positions.ElementAtOrDefault(amountIndex);
            var boxIndex = boundaries.BinarySearch(addPosition);

            if (boxIndex < 0)
            {
                boxIndex = Math.Min(~boxIndex, sortedBoxes.Count - 1);
            }

            var boxOptions = sortedBoxes.Skip(boxIndex);
            var dividedBoxes = DivideToBoxes(boxOptions, boxSpace, numCopies);

            foreach (var (box, splitAmount) in dividedBoxes)
            {
                yield return (card, splitAmount, source, box);
            }
        }
    }


    private IEnumerable<int> GetAddPositions(IEnumerable<Amount> boxAmounts)
    {
        int amountSum = 0;

        foreach (var shared in boxAmounts)
        {
            yield return amountSum;

            amountSum += shared.NumCopies;
        }
    }


    private IEnumerable<int> GetBoxBoundaries(IEnumerable<Box> boxes)
    {
        int capacitySum = 0;

        foreach (var box in boxes)
        {
            capacitySum += box.Capacity;

            yield return capacitySum;
        }
    }


    private void ApplyNewReturns(
        IEnumerable<(Card, int, Deck?, Box)> returnPairs,
        IList<Amount> newAmounts,
        IList<Change> changes,
        IDictionary<int, int> boxSpace)
    {
        foreach (var (card, numCopies, source, box) in returnPairs)
        {
            var newSpot = new Amount
            {
                Card = card,
                Location = box,
                NumCopies = numCopies
            };

            var newChange = new Change
            {
                Card = card,
                To = box,
                From = source,
                Amount = numCopies
            };

            newAmounts.Add(newSpot);
            changes.Add(newChange);

            boxSpace[box.Id] += numCopies;
        }
    }

    #endregion



    private IEnumerable<(Box Box, int Amount)> FitToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<int, int> boxSpace,
        int cardsToAssign)
    {
        foreach (var box in boxes)
        {
            int spaceUsed = boxSpace.GetValueOrDefault(box.Id);
            int remainingSpace = Math.Max(0, box.Capacity - spaceUsed);

            var newNumCopies = Math.Min(cardsToAssign, remainingSpace);

            if (newNumCopies == 0)
            {
                continue;
            }

            yield return (box, newNumCopies);

            cardsToAssign -= newNumCopies;

            if (cardsToAssign == 0)
            {
                yield break;
            }
        }
    }


    private IEnumerable<(Box, int)> DivideToBoxes(
        IEnumerable<Box> boxes,
        IReadOnlyDictionary<int, int> boxSpace,
        int cardsToAssign)
    {
        var fitInBoxes = FitToBoxes(boxes.SkipLast(1), boxSpace, cardsToAssign);

        foreach (var fit in fitInBoxes)
        {
            cardsToAssign -= fit.Amount;

            yield return fit;

            if (cardsToAssign == 0)
            {
                yield break;
            }
        }

        yield return (boxes.Last(), cardsToAssign);
    }



    #region Optimize

    public async Task<Transaction?> OptimizeAsync(CancellationToken cancel = default)
    {
        await _lock.WaitAsync(cancel);

        try
        {
            var sortedBoxes = await SortedBoxes.ToArrayAsync(cancel); // unbounded: keep eye on

            if (!sortedBoxes.Any())
            {
                return null;
            }

            var sortedAmounts = await SortedAmounts.ToArrayAsync(cancel); // unbounded: keep eye on

            if (!sortedAmounts.Any())
            {
                return null;
            }

            var optimizeChanges = ApplyOptimizeChanges(sortedAmounts, sortedBoxes);

            if (optimizeChanges is not null)
            {
                // intentionally throw db exception
                await _dbContext.SaveChangesAsync(cancel);
            }

            return optimizeChanges;
        }
        finally
        {
            _lock.Release();
        }
    }


    private Transaction? ApplyOptimizeChanges(
        IReadOnlyList<Amount> sharedAmounts, 
        IReadOnlyList<Box> sortedBoxes)
    {
        var (transaction, updatedAmounts) = AddUpdatedBoxAmounts(sharedAmounts, sortedBoxes);

        if (!transaction.Changes.Any())
        {
            return null;
        }

        var addedAmounts = updatedAmounts.Except(sharedAmounts);
        var emptyAmounts = sharedAmounts.Where(ca => ca.NumCopies == 0);

        _dbContext.Transactions.Attach(transaction);

        _dbContext.Amounts.AttachRange(addedAmounts);
        _dbContext.Amounts.RemoveRange(emptyAmounts);

        return transaction;
    }


    private (Transaction, IReadOnlyCollection<Amount>) AddUpdatedBoxAmounts(
        IReadOnlyList<Amount> sortedAmounts, 
        IReadOnlyList<Box> sortedBoxes)
    {
        var newTransaction = new Transaction();
        var changes = newTransaction.Changes;

        var boxAmounts = sortedAmounts
            .ToDictionary(a => (a.CardId, a.LocationId));

        var oldAmounts = sortedAmounts
            .Where(a => a.NumCopies > 0)
            .Select(a => (a.Card, a.Location, a.NumCopies))
            .ToList();

        foreach (var shared in sortedAmounts)
        {
            shared.NumCopies = 0;
        }

        var boxSpace = sortedBoxes // faster than summing every time
            .ToDictionary(b => b.Id, _ => 0);

        int boxStart = 0;

        foreach (var (card, oldBox, oldNumCopies) in oldAmounts)
        {
            var boxOptions = sortedBoxes
                .Concat(sortedBoxes)
                .Skip(boxStart)
                .Take(sortedBoxes.Count);

            var newBoxAmounts = DivideToBoxes(boxOptions, boxSpace, oldNumCopies);

            bool multipleBoxes = false;

            foreach (var (newBox, newNumCopies) in newBoxAmounts)
            {
                var boxAmount = GetOrAddBoxAmount(boxAmounts, card, newBox);

                boxAmount.NumCopies += newNumCopies;
                boxSpace[newBox.Id] += newNumCopies;

                if (!multipleBoxes)
                {
                    multipleBoxes = true;
                }
                else
                {
                    boxStart++;
                }

                if (oldBox != newBox)
                {
                    changes.Add(new()
                    {
                        Card = card,
                        From = oldBox,
                        To = newBox,
                        Amount = newNumCopies
                    });
                }
            }
        }

        return (newTransaction, boxAmounts.Values);
    }


    private Amount GetOrAddBoxAmount(
        IDictionary<(string, int), Amount> boxAmounts, Card card, Box box)
    {
        var cardBox = (card.Id, box.Id);

        if (!boxAmounts.TryGetValue(cardBox, out var boxAmount))
        {
            boxAmount = new()
            {
                Card = card,
                Location = box
            };

            boxAmounts.Add(cardBox, boxAmount);
        }

        return boxAmount;
    }

    #endregion
}