using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;
using MTGViewer.Services.Treasury;

namespace MTGViewer.Services;

public class SortedPartitionTreasury : ITreasuryQuery
{
    private readonly CardDbContext _dbContext;
    private readonly IDbContextFactory<CardDbContext> _dbFactory;
    private readonly ILogger<SortedPartitionTreasury> _logger;

    public SortedPartitionTreasury(
        CardDbContext dbContext, 
        IDbContextFactory<CardDbContext> dbFactory, 
        ILogger<SortedPartitionTreasury> logger)
    {
        _dbContext = dbContext;
        _dbFactory = dbFactory;
        _logger = logger;
    }


    public IQueryable<Box> Boxes => 
        _dbContext.Boxes
            .AsNoTrackingWithIdentityResolution();

    public IQueryable<Amount> Cards => 
        _dbContext.Amounts
            .Where(ca => ca.Location is Box)
            .AsNoTrackingWithIdentityResolution();



    #region Queries

    private static IQueryable<Amount> CheckoutTargets(
        CardDbContext dbContext, IEnumerable<CardRequest> requests)
    {
        var requestIds = requests
            .Select(cr => cr.Card.Id)
            .ToArray();

        var requestCards = requests
            .Select(cr => cr.Card);

        dbContext.Cards.AttachRange(requestCards);

        // track requests for modifications
        // also to keep original Card since it should not be modified

        return dbContext.Amounts
            .Where(a => a.Location is Box
                && a.NumCopies > 0
                && requestIds.Contains(a.CardId))

            .Include(a => a.Card)
            .Include(a => a.Location)
            .OrderBy(a => a.NumCopies);
    }


    private static IQueryable<Box> OrderedBoxes(
        CardDbContext dbContext, IEnumerable<CardRequest>? requests = null)
    {
        if (requests is not null)
        {
            var cards = requests.Select(cr => cr.Card);

            dbContext.Cards.AttachRange(cards);
        }

        // loading all shared cards, could be memory inefficient
        // TODO: find more efficient way to determining card position
        // unbounded: keep eye on

        return dbContext.Boxes
            .Include(b => b.Cards
                .OrderBy(a => a.NumCopies))
                .ThenInclude(a => a.Card)
            .OrderBy(b => b.Id);
    }

    #endregion



    #region Checkout

    public async Task<RequestResult> RequestCheckoutAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default)
    {
        requests = AsCheckoutRequests(requests);

        if (!requests.Any())
        {
            return RequestResult.Empty;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        return await GetCheckoutsAsync(dbContext, requests, cancel);
    }


    private static IReadOnlyList<CardRequest> AsCheckoutRequests(IEnumerable<CardRequest?>? requests)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        var requestArray = requests.ToArray();

        if (requestArray.Any(cr => cr is null))
        {
            throw new ArgumentNullException(nameof(requests));
        }

        return requestArray
            .Cast<CardRequest>()
            .GroupBy(
                req => req.Card.Id,
                (_, requests) => requests.First() with
                { 
                    NumCopies = requests.Sum(req => req.NumCopies) 
                })
            .ToList();
    }


    private static async Task<RequestResult> GetCheckoutsAsync(
        CardDbContext dbContext,
        IEnumerable<CardRequest> checkouts,
        CancellationToken cancel)
    {
        var targets = await CheckoutTargets(dbContext, checkouts).ToListAsync(cancel);

        if (!targets.Any())
        {
            return RequestResult.Empty;
        }

        ApplyExactCheckouts(targets, checkouts);
        ApplyApproxCheckouts(targets, checkouts);

        return GetRequestResult(dbContext);
    }


    private static void ApplyExactCheckouts(
        IEnumerable<Amount> targets, IEnumerable<CardRequest> checkouts)
    {
        var exactMatches = checkouts
            .GroupJoin( targets,
                req => req.Card.Id,
                tar => tar.CardId,
                (request, matches) => (request, matches));

        foreach (var (request, matches) in exactMatches)
        {
            ApplyTargetRequests(request, matches);
        }
    }


    private static void ApplyApproxCheckouts(
        IEnumerable<Amount> targets, IEnumerable<CardRequest> checkouts)
    {
        var checkoutsByName = checkouts
            .Where(req => req.NumCopies > 0)
            .GroupBy(req => req.Card.Name,
                (_, requests) => requests.First() with
                {
                    // just truncate since Card name is only 
                    // thing that matters
                    // keep eye on
                    NumCopies = requests.Sum(req => req.NumCopies)
                });

        var targetsByName = targets
            .Where(a => a.NumCopies > 0)
            .GroupBy(a => a.Card.Name,
                (_, amounts) => new CardNameGroup(amounts));

        var approxMatches = checkoutsByName 
            .Join( targetsByName,
                check => check.Card.Name,
                matches => matches.Name,
                (check, matches) => (check, matches));

        foreach (var (request, matches) in approxMatches)
        {
            ApplyTargetRequests(request, matches);
        }
    }


    private static void ApplyTargetRequests(
        CardRequest request, IEnumerable<Amount> targets)
    {
        using var e = targets.GetEnumerator();

        while (request.NumCopies > 0 && e.MoveNext())
        {
            var target = e.Current;
            int amountTaken = Math.Min(request.NumCopies, target.NumCopies);

            if (amountTaken == 0)
            {
                continue;
            }

            target.NumCopies -= amountTaken;
            request.NumCopies -= amountTaken;
        }
    }

    #endregion



    #region Return

    public async Task<RequestResult> RequestReturnAsync(
        IEnumerable<CardRequest> requests, 
        CancellationToken cancel = default)
    {
        requests = AsReturnRequests(requests); 

        if (!requests.Any())
        {
            return RequestResult.Empty;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        return await GetReturnsAsync(dbContext, requests, cancel);
    }


    private static IReadOnlyList<CardRequest> AsReturnRequests(IEnumerable<CardRequest?>? requests)
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


    private static async Task<RequestResult> GetReturnsAsync(
        CardDbContext dbContext,
        IEnumerable<CardRequest> returns,
        CancellationToken cancel)
    {
        var boxes = await OrderedBoxes(dbContext, returns).ToListAsync(cancel); 
        if (!boxes.Any())
        {
            throw new InvalidOperationException("There are no boxes to return to");
        }

        AddMissingExcess(boxes);

        var treasuryContext = new TreasuryContext(boxes);

        ApplyReturnsExact(treasuryContext, returns);
        ApplyReturnsApprox(treasuryContext, returns);

        ApplyReturnsNew(treasuryContext, returns);

        TransferExcessExact(treasuryContext);
        TransferExcessApprox(treasuryContext);
        // overflow should not be generated from returns

        return GetRequestResult(dbContext, treasuryContext);
    }


    private static void ApplyReturnsExact(
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
                treasuryContext.ReturnCopies(card, box, splitCopies);
                // changes will cascade to the split iter via boxSpace

                request.NumCopies -= splitCopies;
            }
        }
    }


    private static void ApplyReturnsApprox(
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
                treasuryContext.ReturnCopies(card, box, splitCopies);
                // changes will cascade to the split iter via boxSpace

                request.NumCopies -= splitCopies;
            }
        }
    }


    private static void ApplyReturnsNew(
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

            var bestBoxes = boxSearch.FindBestBoxes(card);

            var newReturns = FitToBoxes(bestBoxes, boxSpace, numCopies, excess);

            foreach ((Box box, int splitCopies) in newReturns)
            {
                // returnContext changes will cascade to the split iter via boxSpace

                treasuryContext.ReturnCopies(card, box, splitCopies);
            }
        }
    }

    #endregion



    #region Box Update

    public async Task<RequestResult> RequestUpdateAsync(Box updated, CancellationToken cancel = default)
    {
        if (updated is null)
        {
            throw new ArgumentNullException(nameof(updated));
        }

        if (updated.IsExcess)
        {
            return RequestResult.Empty;
        }

        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancel);

        var updateState = await GetUpdateStateAsync(dbContext, updated, cancel);

        if (updateState is UpdateState.Invalid)
        {
            return RequestResult.Empty;
        }

        return await GetUpdatesAsync(dbContext, updateState, updated, cancel);
    }


    private enum UpdateState
    {
        Invalid,
        Add,
        Lower,
        Increase
    }


    private static async Task<UpdateState> GetUpdateStateAsync(
        CardDbContext dbContext, 
        Box updated, 
        CancellationToken cancel)
    {
        if (updated.Id == default)
        {
            return UpdateState.Add;
        }

        var boxEntry = dbContext.Entry(updated);
        var capacityMeta = boxEntry.Property(b => b.Capacity).Metadata;

        var dbValues = await boxEntry.GetDatabaseValuesAsync(cancel);

        return dbValues?.GetValue<int>(capacityMeta) switch
        {
            int dbCapacity when dbCapacity < updated.Capacity => UpdateState.Increase,
            int dbCapacity when dbCapacity > updated.Capacity => UpdateState.Lower,
            _ => UpdateState.Invalid
        };
    }


    private static async Task<RequestResult> GetUpdatesAsync(
        CardDbContext dbContext, 
        UpdateState updateState,
        Box updated, 
        CancellationToken cancel)
    {
        List<Box> boxes;        

        if (updateState is UpdateState.Add)
        {
            boxes = await OrderedBoxes(dbContext)
                .AsAsyncEnumerable()
                .Append(updated)
                .ToListAsync(cancel);
        }
        else
        {
            dbContext.Attach(updated);

            boxes = await OrderedBoxes(dbContext).ToListAsync(cancel);
        }

        if (!boxes.Any())
        {
            return RequestResult.Empty;
        }

        AddMissingExcess(boxes);

        var treasuryContext = new TreasuryContext(boxes);

        TransferExcessExact(treasuryContext);
        TransferExcessApprox(treasuryContext);

        TransferOverflow(treasuryContext);

        return GetRequestResult(dbContext, treasuryContext);
    }

    #endregion



    private static void AddMissingExcess(List<Box> boxes)
    {
        if (!boxes.Any(b => b.IsExcess))
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
            
            boxes.Add(excessBox);
        }
    }


    private static void TransferExcessExact(TreasuryContext treasuryContext)
    {
        var (available, _, excessBoxes, boxSpace) = treasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);
        var excessAmounts = excessBoxes.SelectMany(b => b.Cards);
        var excessCards = excessAmounts.Select(a => a.Card);

        if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
        {
            return;
        }

        using var e = excessAmounts.GetEnumerator();

        // TODO: account for changing NumCopies while iter
        var exactRebalance = ExactLookup(availableAmounts, excessCards, boxSpace);

        while (available.Any() && e.MoveNext())
        {
            var excess = e.Current;
            var bestBoxes = exactRebalance[excess.CardId];

            if (!bestBoxes.Any())
            {
                continue;
            }

            var boxTransfers = FitToBoxes(bestBoxes, boxSpace, excess.NumCopies);

            foreach ((Box box, int splitCopies) in boxTransfers)
            {
                treasuryContext.ReturnCopies(excess.Card, box, splitCopies);
                excess.NumCopies -= splitCopies;
            }
        }
    }


    private static void TransferExcessApprox(TreasuryContext treasuryContext)
    {
        var (available, _, excessBoxes, boxSpace) = treasuryContext;

        var availableAmounts = available.SelectMany(b => b.Cards);
        var excessAmounts = excessBoxes.SelectMany(b => b.Cards);
        var excessCards = excessAmounts.Select(a => a.Card);

        if (!available.Any() || excessAmounts.All(a => a.NumCopies == 0))
        {
            return;
        }

        using var e = excessAmounts.GetEnumerator();

        var exactRebalance = ApproxLookup(availableAmounts, excessCards, boxSpace);

        while (available.Any() && e.MoveNext())
        {
            var excess = e.Current;
            var bestBoxes = exactRebalance[excess.Card.Name].Union(available);

            if (!bestBoxes.Any())
            {
                continue;
            }

            var boxTransfers = FitToBoxes(bestBoxes, boxSpace, excess.NumCopies);

            foreach ((Box box, int splitCopies) in boxTransfers)
            {
                treasuryContext.ReturnCopies(excess.Card, box, splitCopies);
                excess.NumCopies -= splitCopies;
            }
        }
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
                treasuryContext.ReturnCopies(overflow.Card, box, splitCopies);
                overflow.NumCopies -= splitCopies;
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


    private static RequestResult GetRequestResult(
        CardDbContext dbContext,
        TreasuryContext? treasuryContext = null)
    {
        var changeTracker = dbContext.ChangeTracker;
        var amountEntries = changeTracker.Entries<Amount>();

        var treasuryAmounts = treasuryContext?.AddedAmounts() ?? Enumerable.Empty<Amount>();

        var amounts = amountEntries
            .Where(e => e.State is EntityState.Modified
                && e.Entity.Location is Box)
            .Select(e => e.Entity)
            .Union(treasuryAmounts)
            .ToList();

        if (!amounts.Any())
        {
            return RequestResult.Empty;
        }

        bool autoDetect = changeTracker.AutoDetectChangesEnabled;

        changeTracker.AutoDetectChangesEnabled = false;

        var originalCopies = amountEntries
            .IntersectBy(amounts, e => e.Entity)
            .Select(e => 
                (e.Entity.Id, e.Property(a => a.NumCopies).OriginalValue))
            .ToDictionary(
                io => io.Id, io => io.OriginalValue);

        changeTracker.AutoDetectChangesEnabled = autoDetect;

        return new RequestResult(amounts, originalCopies);
    }

}
